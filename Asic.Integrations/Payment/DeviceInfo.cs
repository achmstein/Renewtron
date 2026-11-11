namespace Asic.Integrations.Payment;

public class DeviceInfo
{
    public required string AcceptHeader { get; set; }
    public int ColorDepth { get; set; }
    public int ScreenHeight { get; set; }
    public int ScreenWidth { get; set; }
    public required string Locale { get; set; }
    public bool JavaEnabled { get; set; }
    public int TimezoneOffsetUtcMinutes { get; set; }
    public required string IpAddress { get; set; }
    public required string UserAgent { get; set; }
}