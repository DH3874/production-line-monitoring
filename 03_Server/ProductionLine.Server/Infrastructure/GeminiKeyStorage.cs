using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ProductionLine.Server.Infrastructure;

internal static class GeminiKeyStorage
{
    internal static string StoragePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProductionLine", "gemini-key.dpapi");

    // 환경변수 등 명시 설정이 없을 때 현재 Windows 계정에 저장한 키를 읽는다.
    internal static string Load(string? filePath = null)
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        filePath ??= StoragePath;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length is < 1 or > 65536) return string.Empty;
            var cipher = File.ReadAllBytes(filePath);
            var entropy = Encoding.UTF8.GetBytes("ProductionLine.Gemini.v1");
            var input = Allocate(cipher);
            var additional = Allocate(entropy);
            DataBlob output = default;
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, ref additional, IntPtr.Zero, IntPtr.Zero,
                    1 /* CRYPTPROTECT_UI_FORBIDDEN */, out output)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var plain = new byte[output.Size];
                try
                {
                    Marshal.Copy(output.Data, plain, 0, plain.Length);
                    var key = Encoding.UTF8.GetString(plain).Trim();
                    return key.Length is > 0 and <= 512 && !key.Any(char.IsWhiteSpace) ? key : string.Empty;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                    // DPAPI가 반환한 평문 버퍼도 해제 전에 지운다.
                    for (var i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                Marshal.FreeHGlobal(additional.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or CryptographicException)
        {
            return string.Empty;
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new() { Size = bytes.Length, Data = data };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
