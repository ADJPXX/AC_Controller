using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AC_Controller.Services;

internal static class WindowsDataProtection
{
    private const int CryptprotectUiForbidden = 0x1;
    private static readonly byte[] Entropy = "AC_Controller.Session.v1"u8.ToArray();

    public static byte[] Protect(byte[] data) => Transform(data, protect: true);

    public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = CreateBlob(data);
        var entropy = CreateBlob(Entropy);
        DataBlob output = default;

        try
        {
            var succeeded = protect
                ? CryptProtectData(ref input, "AC Controller", ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out output);

            if (!succeeded)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "O Windows não conseguiu proteger a sessão do aparelho.");

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            FreeBlob(ref input);
            FreeBlob(ref entropy);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void FreeBlob(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;

        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
