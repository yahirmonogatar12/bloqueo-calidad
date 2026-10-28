using FluentAssertions;
using Moq;
using QualityLock.Application.Interfaces;
using QualityLock.Application.Services;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Tests;

/// <summary>Verificacion MES antes de teclear en ICT: solo EBR cuya ultima Vision fue OK.</summary>
public class VisionCheckServiceTests
{
    private const string Barcode = "EBR43713602922601050219";
    private readonly Mock<IVisionHistoryRepository> _visionRepo = new();
    private readonly Mock<IVisionRejectionRepository> _rejectionRepo = new();
    private readonly VisionCheckService _sut;

    public VisionCheckServiceTests() => _sut = new VisionCheckService(_visionRepo.Object, _rejectionRepo.Object);

    private VisionRejection SavedRejection()
        => (VisionRejection)_rejectionRepo.Invocations.Single(i => i.Method.Name == "AddAsync").Arguments[0];

    private void LastInspection(string? result)
        => _visionRepo.Setup(r => r.GetLastAsync(Barcode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result is null ? null : new VisionInspection { SourceUid = "uid1", Result = result, Machine = "M1" });

    private void Defects(params VisionDefect[] defects)
        => _visionRepo.Setup(r => r.GetDefectsAsync("uid1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(defects);

    [Fact]
    public async Task LastNg_ReasonSaysWhatFailed()
    {
        LastInspection("NG");
        Defects(
            new VisionDefect { TestName = "Conector", Position = "2", SubTest = "Maker-Direction-Percent", ValueText = "84.2645", ValueNum = 84.2645 },
            new VisionDefect { TestName = "Capacitor ceramico", Position = "1", SubTest = "Direction-Marker", ValueText = "cantidad:1,coincidencia:56.34577% <- NG", ValueNum = 56.34577 },
            new VisionDefect { TestName = "Barcode (ASSY)", ValueText = "no leyo la etiqueta" });

        var r = await _sut.CheckAsync(Barcode);

        r.Allowed.Should().BeFalse();
        r.Reason.Should().Be(
            "Última inspección de Visión: NG. Falla: Conector 2: Maker-Direction-Percent 84.26; " +
            "Capacitor ceramico 1: Direction-Marker 56.35% (+1 más).");
    }

    [Fact]
    public async Task LastNg_DetailLookupFails_StillDenied()
    {
        LastInspection("NG");
        _visionRepo.Setup(r => r.GetDefectsAsync("uid1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));

        var r = await _sut.CheckAsync(Barcode);

        r.Allowed.Should().BeFalse();
        r.Reason.Should().Be("Última inspección de Visión: NG.");
    }

    [Fact]
    public async Task LastOk_DoesNotQueryDefects()
    {
        LastInspection("OK");
        await _sut.CheckAsync(Barcode);
        _visionRepo.Verify(x => x.GetDefectsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LastOk_IsAllowed()
    {
        LastInspection("OK");
        var r = await _sut.CheckAsync(Barcode);
        r.Allowed.Should().BeTrue();
        r.LastResult.Should().Be("OK");
    }

    [Fact]
    public async Task LastNg_IsDenied()
    {
        LastInspection("NG");
        var r = await _sut.CheckAsync(Barcode);
        r.Allowed.Should().BeFalse();
        r.Reason.Should().Contain("NG");
    }

    [Fact]
    public async Task NeverInspected_IsDenied()
    {
        LastInspection(null);
        var r = await _sut.CheckAsync(Barcode);
        r.Allowed.Should().BeFalse();
        r.Reason.Should().Contain("Sin registro");
    }

    [Fact]
    public async Task NonEbrCode_IsDenied_WithoutQueryingVision()
    {
        var r = await _sut.CheckAsync("I20220203-0072-00002;MAIN;EBR32881228;1;");
        r.Allowed.Should().BeFalse();
        _visionRepo.Verify(x => x.GetLastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Registro de rechazos (vision_check_rejections_QA) ──

    [Fact]
    public async Task NeverInspected_SavesRejectionWithStationAndLine()
    {
        LastInspection(null);

        await _sut.CheckAsync(Barcode, "ICT-01", "M1");

        var saved = SavedRejection();
        saved.Should().BeEquivalentTo(new
        {
            StationCode = "ICT-01", Line = "M1", Code = Barcode, Type = "SIN_HISTORIAL",
            Reason = "Sin registro en Visión.", Machine = (string?)null,
        });
        saved.RejectedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task LastNg_SavesRejectionWithReasonAndMachine()
    {
        LastInspection("NG");
        Defects(new VisionDefect { TestName = "Conector", Position = "CON101", SubTest = "Maker_Axis", ValueText = "47.95%", ValueNum = 47.95 });

        var r = await _sut.CheckAsync(Barcode, "ICT-01", "M1");

        var saved = SavedRejection();
        saved.Type.Should().Be("NG");
        saved.Reason.Should().Be(r.Reason).And.Contain("Conector CON101: Maker_Axis 47.95%");
        saved.Machine.Should().Be("M1");
    }

    [Fact]
    public async Task NonEbrCode_SavesRejection()
    {
        await _sut.CheckAsync("I20261022-026-00737", "ICT-01", "M3");
        SavedRejection().Type.Should().Be("NO_EBR");
    }

    [Fact]
    public async Task Allowed_DoesNotSaveRejection()
    {
        LastInspection("OK");
        await _sut.CheckAsync(Barcode, "ICT-01", "M1");
        _rejectionRepo.Verify(x => x.AddAsync(It.IsAny<VisionRejection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SavingRejectionFails_StillDenied()
    {
        LastInspection(null);
        _rejectionRepo.Setup(x => x.AddAsync(It.IsAny<VisionRejection>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Table 'vision_check_rejections_QA' doesn't exist"));

        var r = await _sut.CheckAsync(Barcode, "ICT-01", "M1");

        r.Allowed.Should().BeFalse();
        r.Reason.Should().Be("Sin registro en Visión.");
    }

    [Fact]
    public async Task RepeatedScan_SavesOneRowPerScanWithItsTime()
    {
        var t1 = new DateTime(2026, 10, 26, 14, 29, 55, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(23);

        await _sut.RecordRepeatedScanAsync("ICT-02-03", new RepeatedScanRequest(
            " " + Barcode + " ", "M2", [new RepeatedScanItem(1, t1), new RepeatedScanItem(2, t2)]));

        var saved = _rejectionRepo.Invocations
            .Where(i => i.Method.Name == "AddAsync")
            .Select(i => (VisionRejection)i.Arguments[0])
            .ToList();
        saved.Should().HaveCount(2);
        saved.Select(r => r.RejectedAtUtc).Should().Equal(t1, t2);
        saved.Should().AllSatisfy(r =>
        {
            r.Type.Should().Be("REPETIDO");
            r.Code.Should().Be(Barcode);
            r.StationCode.Should().Be("ICT-02-03");
            r.Line.Should().Be("M2");
        });
        saved[1].Reason.Should().Be("Escaneo 2 de la misma PCB en la sesión del escáner COM.");
    }

    [Fact]
    public async Task Code_IsTrimmedBeforeLookup()
    {
        LastInspection("OK");
        var r = await _sut.CheckAsync("  " + Barcode + " ");
        r.Allowed.Should().BeTrue();
    }
}
