using FluentAssertions;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Input;

namespace QualityLock.Application.Tests;

/// <summary>PCB escaneada 2+ veces en una sesion del escaner COM: que escaneos se registran.</summary>
public class ScanRepeatTrackerTests
{
    private const string Pcb = "EBR30299372922610260001";
    private static readonly DateTime T0 = new(2026, 10, 26, 14, 29, 55, DateTimeKind.Utc);

    [Fact]
    public void FirstScan_IsNotReported()
        => new ScanRepeatTracker().Add(Pcb, T0).Should().BeEmpty();

    [Fact]
    public void SecondScan_ReportsBothWithTheirTimes()
    {
        var tracker = new ScanRepeatTracker();
        tracker.Add(Pcb, T0);

        tracker.Add(Pcb, T0.AddSeconds(23)).Should().Equal(
            new RepeatedScanItem(1, T0),
            new RepeatedScanItem(2, T0.AddSeconds(23)));
    }

    [Fact]
    public void ThirdScan_ReportsOnlyTheNewOne()
    {
        var tracker = new ScanRepeatTracker();
        tracker.Add(Pcb, T0);
        tracker.Add(Pcb, T0.AddSeconds(23));

        tracker.Add(Pcb, T0.AddSeconds(88)).Should().Equal(new RepeatedScanItem(3, T0.AddSeconds(88)));
    }

    [Fact]
    public void OtherPcbs_AreCountedApart()
    {
        var tracker = new ScanRepeatTracker();
        tracker.Add(Pcb, T0);
        tracker.Add("EBR30299372922610260002", T0.AddSeconds(5)).Should().BeEmpty();
    }

    [Fact]
    public void Reset_StartsANewSession()
    {
        var tracker = new ScanRepeatTracker();
        tracker.Add(Pcb, T0);
        tracker.Reset();
        tracker.Add(Pcb, T0.AddMinutes(1)).Should().BeEmpty();
    }
}
