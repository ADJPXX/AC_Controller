using System.IO;

namespace AC_Controller.Services;

public static class GreeResources
{
    public const string Ip = "192.168.18.104";

    public const string Mac = "9424b8844011";

    public static readonly string DeviceKey = GetDeviceKey();

    public const string GcmAad = "qualcomm-test";

    public const int Port = 7000;

    public const int ReceiveTimeoutSeconds = 5;

    public static readonly byte[] GcmIv =
    [
        0x54, 0x40, 0x78, 0x44,
        0x49, 0x67, 0x5A, 0x51,
        0x6C, 0x5E, 0x63, 0x13
    ];

    public static readonly string[] StatusColumns =
    [
        "Pow",
        "Mod",
        "SetTem",
        "WdSpd",
        "Air",
        "Blo",
        "Health",
        "SwhSlp",
        "Lig",
        "SwingLfRig",
        "SwUpDn",
        "Quiet",
        "Tur",
        "StHt",
        "TemUn",
        "HeatCoolType",
        "TemRec",
        "SvSt"
    ];


    private static string GetDeviceKey()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceKey.txt");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("\"DeviceKey.txt\" not found");
        }

        return File.ReadAllText(path);
    }
}