namespace Asic.Client.Models;

public class DeviceInfo
{
    public string AcceptHeader { get; set; }
    public int ColorDepth { get; set; }
    public int ScreenHeight { get; set; }
    public int ScreenWidth { get; set; }
    public string Locale { get; set; }
    public bool JavaEnabled { get; set; }
    public int TimezoneOffsetUtcMinutes { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
}