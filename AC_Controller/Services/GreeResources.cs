using System.Text;

namespace AC_Controller.Services;

public static class GreeResources
{
    public const string GcmAad = "qualcomm-test";
    public static byte[] GcmAadBytes => Encoding.UTF8.GetBytes(GcmAad);
    public const int Port = 7000;
    public const int ReceiveTimeoutSeconds = 5;

    // Fixed by the Gree GCM wire protocol; changing it breaks compatibility with the appliance.
    public static byte[] GcmIv =>
    [
        0x54, 0x40, 0x78, 0x44,
        0x49, 0x67, 0x5A, 0x51,
        0x6C, 0x5E, 0x63, 0x13
    ];

    public static string[] StatusColumns =>
    [
        "Pow", "Mod", "SetTem", "WdSpd", "Blo", "Health", "SwhSlp", "Lig",
        "SwingLfRig", "SwUpDn", "Quiet", "Tur", "StHt", "TemUn", "SvSt"
    ];
}
