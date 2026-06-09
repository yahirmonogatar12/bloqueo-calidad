namespace QualityLock.Client.WinForms.Models;

public class BypassToken
{
    public string StationCode { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string IssuedBy { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
