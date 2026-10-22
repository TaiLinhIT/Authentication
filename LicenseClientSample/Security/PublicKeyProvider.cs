using LicenseShared;
using System.IO;
using System.Reflection;

namespace LicenseClientSample.Security;

/// <summary>
/// Loads the vendor public keys that this client build trusts. Keys come from:
///  1. an embedded resource compiled into the assembly (highest trust), and
///  2. public-keys.json next to the executable, in the "Keys" folder.
/// The client never reads signing keys from user-writable key stores.
/// </summary>
public sealed class PublicKeyProvider
{
    private readonly Dictionary<string, PublicKeyEntry> _byKeyId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PublicKeyEntry> _byProductId = new(StringComparer.OrdinalIgnoreCase);

    private PublicKeyProvider(IEnumerable<(string? Json, string Source)> inputs)
    {
        var sources = new List<string>();

        foreach (var (json, source) in inputs)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;

            var bundle = PublicKeyBundleSerializer.Deserialize(json);
            if (bundle is null || bundle.Keys.Count == 0) continue;

            bool added = false;
            foreach (var entry in bundle.Keys)
            {
                if (string.IsNullOrWhiteSpace(entry.PublicKeyPem)) continue;

                string keyId;
                try
                {
                    keyId = LicenseCrypto.ComputeKeyIdFromPublicPem(entry.PublicKeyPem);
                }
                catch (FormatException)
                {
                    continue;
                }

                // Reject a bundle entry whose declared key id does not match its key material.
                if (!string.IsNullOrWhiteSpace(entry.KeyId) &&
                    !string.Equals(entry.KeyId, keyId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entry.KeyId = keyId;

                if (_byKeyId.TryAdd(keyId, entry))
                {
                    _byProductId.TryAdd(entry.ProductId, entry);
                    added = true;
                }
            }

            if (added) sources.Add(source);
        }

        Sources = sources;
    }

    public IReadOnlyList<string> Sources { get; }

    public int Count => _byKeyId.Count;

    public bool IsEmpty => Count == 0;

    public static PublicKeyProvider LoadDefault()
    {
        var inputs = new List<(string?, string)>();

        var assembly = Assembly.GetExecutingAssembly();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith("PublicKeys.json", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                inputs.Add((reader.ReadToEnd(), "embedded:" + name));
            }
            catch (IOException)
            {
                // Ignore an unreadable embedded bundle.
            }
        }

        foreach (string path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "Keys", "public-keys.json"),
                     Path.Combine(AppContext.BaseDirectory, "public-keys.json")
                 })
        {
            try
            {
                if (File.Exists(path)) inputs.Add((File.ReadAllText(path), path));
            }
            catch (IOException)
            {
                // Ignore an unreadable file; the UI will report that no keys are installed.
            }
        }

        return new PublicKeyProvider(inputs);
    }

    public PublicKeyEntry? Find(string? keyId, string productId)
    {
        if (!string.IsNullOrWhiteSpace(keyId) && _byKeyId.TryGetValue(keyId, out var byKey))
            return byKey;

        return _byProductId.TryGetValue(productId, out var byProduct) ? byProduct : null;
    }
}
