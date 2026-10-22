using System.Security.Cryptography;

namespace LicenseShared;

/// <summary>
/// Windows DPAPI (current user) helper. Adds integrity and confidentiality for local
/// license and clock state files. Optional entropy binds a file to product + machine.
/// </summary>
public static class Dpapi
{
    public static byte[] Protect(byte[] data, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI requires Windows.");

        return ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
    }

    public static byte[]? TryUnprotect(byte[] data, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static byte[] Entropy(string purpose, params string[] values)
    {
        var sb = new System.Text.StringBuilder(purpose);
        foreach (string value in values) sb.Append('|').Append(value);

        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
