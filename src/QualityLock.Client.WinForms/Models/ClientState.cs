namespace QualityLock.Client.WinForms.Models;

public class ClientState
{
    public int CrashCounter { get; set; }
    public DateTime? LastCrashUtc { get; set; }
    public Guid? LastSessionId { get; set; }
    public bool SafeMode { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
