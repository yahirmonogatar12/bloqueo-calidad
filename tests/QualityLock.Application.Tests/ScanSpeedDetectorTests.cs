using FluentAssertions;
using QualityLock.Shared.Input;

namespace QualityLock.Application.Tests;

/// <summary>
/// Verifica la logica de distincion escaner-vs-humano. Como el detector usa un
/// Stopwatch real, simulamos los gaps con esperas cortas (escaner) y largas (humano).
/// </summary>
public class ScanSpeedDetectorTests
{
    [Fact]
    public void SingleChar_IsNotScan()
    {
        var d = new ScanSpeedDetector(40);
        d.RecordKey();
        // Un solo caracter no se puede medir -> por seguridad, no es escaneo.
        d.LooksLikeScan().Should().BeFalse();
    }

    [Fact]
    public void FastUniformKeys_LookLikeScan()
    {
        var d = new ScanSpeedDetector(maxAvgKeyMs: 40);
        // 5 teclas casi sin pausa (rafaga tipo escaner).
        for (int i = 0; i < 5; i++) d.RecordKey();

        d.LastAvgGapMs.Should().BeLessThan(40);
        d.LooksLikeScan().Should().BeTrue();
    }

    [Fact]
    public void SlowHumanKeys_DoNotLookLikeScan()
    {
        var d = new ScanSpeedDetector(maxAvgKeyMs: 40);
        // Teclas con ~80 ms de pausa entre cada una (humano).
        d.RecordKey();
        for (int i = 0; i < 4; i++)
        {
            Thread.Sleep(80);
            d.RecordKey();
        }

        d.LastAvgGapMs.Should().BeGreaterThan(40);
        d.LooksLikeScan().Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var d = new ScanSpeedDetector(40);
        for (int i = 0; i < 5; i++) d.RecordKey();
        d.Reset();
        // Tras reset, un solo caracter nuevo no es escaneo.
        d.RecordKey();
        d.LooksLikeScan().Should().BeFalse();
    }
}
