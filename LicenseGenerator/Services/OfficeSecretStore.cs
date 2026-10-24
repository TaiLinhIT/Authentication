using LicenseShared;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LicenseGenerator.Services;

/// <summary>
/// Holds the per-vendor "office.dat" secret on the issuer machine. The secret is used to
/// seal encrypted license files and must be embedded (as base64) into every client build
/// that has to open them. Stored DPAPI-protected (current user) so it cannot be copied to
/// another account or machine.
/// </summary>
public sealed class OfficeSecretStore
{
    private static ReadOnlySpan<byte> Magic => "WLCOFF1"u8;

    private readonly string _path;

    public OfficeSecretStore(string? path = null)
    {
        _path = path ?? Path.Combine(LicensePaths.Data, "office-secret.bin");
    }

    public string FilePath => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>Reads the secret, or null when none was created yet.</summary>
    public byte[]? Load()
    {
        if (!File.Exists(_path)) return null;

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(_path);
        }
        catch (IOException)
        {
            return null;
        }

        if (raw.Length <= Magic.Length) return null;

        byte[]? plain = Dpapi.TryUnprotect(raw.AsSpan(Magic.Length).ToArray(), Entropy());
        if (plain is null || plain.Length != OfficeLicenseFile.SecretSizeBytes) return null;
        if (!raw.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;

        return plain;
    }

    /// <summary>Returns the existing secret or creates one on first use.</summary>
    public byte[] LoadOrCreate()
    {
        byte[]? existing = Load();
        if (existing is not null) return existing;

        byte[] secret = OfficeLicenseFile.CreateSecret();
        Save(secret);
        return secret;
    }

    /// <summary>Base64 form of the secret, ready to paste into the client application.</summary>
    public string ExportBase64() => Convert.ToBase64String(LoadOrCreate());

    private void Save(byte[] secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        byte[] plain = (byte[])secret.Clone();
        byte[] protectedBlob = Dpapi.Protect(plain, Entropy());

        byte[] blob = new byte[Magic.Length + protectedBlob.Length];
        Magic.CopyTo(blob);
        protectedBlob.CopyTo(blob, Magic.Length);

        LicenseStore.WriteAtomic(_path, blob);
        CryptographicOperations.ZeroMemory(plain);
    }

    private byte[] Entropy() => Dpapi.Entropy("WLC1-OFFICE-SECRET");
}
