using System.Security.Cryptography;

namespace AC_Controller.Services;

internal static class WindowsDataProtection
{
    private static readonly byte[] Entropy = "AC_Controller.Session.v1"u8.ToArray();

    public static byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
