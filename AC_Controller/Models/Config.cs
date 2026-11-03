namespace AC_Controller.Models;

public sealed class Config
{
    public string? DeviceMac { get; init; }
    public string? LastKnownIp { get; init; }
    public bool StartWithWindows { get; init; }
}
