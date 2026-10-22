using System.Text;

namespace LicenseShared;

/// <summary>
/// Persists the installed license for one product. The file is DPAPI-protected and bound to
/// the product and the current machine fingerprint, so copying the file to another PC fails.
/// </summary>
public sealed class LicenseStore
{
    private static ReadOnlySpan<byte> Magic => "WLCLIC1"u8;

    private readonly string _productId;
    private readonly string _path;

    public LicenseStore(string productId, string? root = null)
    {
        _productId = productId ?? throw new ArgumentNullException(nameof(productId));
        string baseRoot = root ?? LicensePaths.Licenses;
        _path = Path.Combine(baseRoot, SafeName(productId), "license.dat");
    }

    public string FilePath => _path;

    public bool Exists => File.Exists(_path);

    public void Save(string licenseText, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(licenseText))
            throw new ArgumentException("License text is empty.", nameof(licenseText));

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        byte[] payload = Encoding.UTF8.GetBytes(licenseText);
        byte[] blob = new byte[Magic.Length + payload.Length];
        Magic.CopyTo(blob);
        payload.CopyTo(blob, Magic.Length);

        byte[] protectedBlob = Dpapi.Protect(blob, Entropy(fingerprint));
        WriteAtomic(_path, protectedBlob);
    }

    public string? Load(string fingerprint)
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

        byte[]? unprotected = Dpapi.TryUnprotect(raw, Entropy(fingerprint));
        if (unprotected is null || unprotected.Length <= Magic.Length) return null;
        if (!unprotected.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;

        return Encoding.UTF8.GetString(unprotected, Magic.Length, unprotected.Length - Magic.Length);
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
            // Best effort; a locked file will be replaced on the next successful save.
        }
    }

    private byte[] Entropy(string fingerprint) => Dpapi.Entropy("WLC1-LICENSE", _productId, fingerprint);

    public static void WriteAtomic(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);

        if (File.Exists(path))
        {
            try
            {
                File.Replace(temp, path, null);
                return;
            }
            catch (IOException)
            {
                File.Delete(path);
            }
        }

        File.Move(temp, path);
    }

    public static string SafeName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');

        string result = sb.ToString().Trim();
        return result.Length == 0 ? "default" : result;
    }
}
