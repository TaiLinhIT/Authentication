using LicenseGenerator.Localization;
using LicenseShared;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicenseGenerator.Services;

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Store = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class ProductStore
{
    private readonly string _path;

    public ProductStore(string? path = null)
    {
        _path = path ?? Path.Combine(LicensePaths.Data, "products.json");
    }

    public string FilePath => _path;

    public List<ProductDefinition> Load()
    {
        if (!File.Exists(_path)) return new List<ProductDefinition>();

        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<ProductDefinition>>(json, JsonSettings.Store)
                   ?? new List<ProductDefinition>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The product catalog is damaged and could not be read:\n{_path}\n\n{ex.Message}", ex);
        }
    }

    public void Save(IEnumerable<ProductDefinition> products)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(products.ToList(), JsonSettings.Store);
        LicenseStore.WriteAtomic(_path, bytes);
    }

    public void Add(ProductDefinition product)
    {
        var products = Load();
        products.Add(product);
        Save(products);
    }

    public void Update(ProductDefinition product)
    {
        var products = Load();
        int index = products.FindIndex(p => p.ProductId == product.ProductId);
        if (index < 0) throw new InvalidOperationException(L.T("S_ProductNoLongerExists"));
        products[index] = product;
        Save(products);
    }

    public void Remove(string productId)
    {
        var products = Load();
        products.RemoveAll(p => p.ProductId == productId);
        Save(products);
    }
}

public sealed class LicenseRecordStore
{
    private readonly string _path;

    public LicenseRecordStore(string? path = null)
    {
        _path = path ?? Path.Combine(LicensePaths.Data, "issued-licenses.json");
    }

    public string FilePath => _path;

    public List<LicenseRecord> Load()
    {
        if (!File.Exists(_path)) return new List<LicenseRecord>();

        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<LicenseRecord>>(json, JsonSettings.Store)
                   ?? new List<LicenseRecord>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The issued-license register is damaged and could not be read:\n{_path}\n\n{ex.Message}", ex);
        }
    }

    public void Save(IEnumerable<LicenseRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            records.OrderByDescending(r => r.IssuedAtUtc).ToList(), JsonSettings.Store);
        LicenseStore.WriteAtomic(_path, bytes);
    }

    public void Add(LicenseRecord record)
    {
        var records = Load();
        records.Add(record);
        Save(records);
    }

    public void Update(LicenseRecord record)
    {
        var records = Load();
        int index = records.FindIndex(r => r.LicenseId == record.LicenseId);
        if (index < 0) throw new InvalidOperationException(L.T("S_RecordNoLongerExists"));
        records[index] = record;
        Save(records);
    }

    public void Remove(string licenseId)
    {
        var records = Load();
        records.RemoveAll(r => r.LicenseId == licenseId);
        Save(records);
    }
}

public sealed class KeyStore
{
    private readonly string _root;

    public KeyStore(string? root = null)
    {
        _root = root ?? LicensePaths.Keys;
    }

    public string Root => _root;

    public string DirectoryFor(string productId) => Path.Combine(_root, LicenseStore.SafeName(productId));

    public string PrivateKeyPath(string productId) => Path.Combine(DirectoryFor(productId), "private-key.p8");

    public string PublicKeyPath(string productId) => Path.Combine(DirectoryFor(productId), "public-key.pem");

    public bool HasKeyPair(string productId) =>
        File.Exists(PrivateKeyPath(productId)) && File.Exists(PublicKeyPath(productId));

    public bool HasAnyPrivateKey()
    {
        if (!Directory.Exists(_root)) return false;
        return Directory.EnumerateDirectories(_root)
            .Any(dir => File.Exists(Path.Combine(dir, "private-key.p8")));
    }

    public ProductDefinition CreateKeyPair(ProductDefinition product, string password)
    {
        product.KeyId = LicenseCrypto.CreateKeyPair(
            product.Algorithm,
            product.KeySize,
            PrivateKeyPath(product.ProductId),
            PublicKeyPath(product.ProductId),
            password);

        return product;
    }

    public string? ReadPublicKey(string productId)
    {
        string path = PublicKeyPath(productId);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool VerifyPassword(string password)
    {
        if (!Directory.Exists(_root)) return false;

        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            string privatePath = Path.Combine(directory, "private-key.p8");
            if (File.Exists(privatePath) && LicenseCrypto.VerifyPrivateKeyPassword(privatePath, password))
                return true;
        }

        return false;
    }

    public void DeleteKeys(string productId)
    {
        string directory = DirectoryFor(productId);
        if (!Directory.Exists(directory)) return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked key file is left in place; the product entry is still removed.
        }
    }

    /// <summary>Number of stored private keys, including keys whose product was deleted.</summary>
    public int StoredKeyCount()
    {
        if (!Directory.Exists(_root)) return 0;
        return Directory.EnumerateDirectories(_root)
            .Count(dir => File.Exists(Path.Combine(dir, "private-key.p8")));
    }

    /// <summary>
    /// Erases every stored key folder. Only allowed when the product catalog is empty, so that the
    /// next product creates a fresh key pair with a new issuer password.
    /// </summary>
    public int DeleteAllKeys()
    {
        if (!Directory.Exists(_root)) return 0;

        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(_root).ToList())
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (IOException)
            {
                // A locked key file is left in place instead of failing the whole reset.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }
}

public sealed class SigningSession
{
    private readonly KeyStore _keys;

    public SigningSession(KeyStore keys)
    {
        _keys = keys;
    }

    public bool IsUnlocked { get; private set; }

    public string? Password { get; private set; }

    public void Unlock(string password)
    {
        Password = password;
        IsUnlocked = true;
    }

    public void Lock()
    {
        Password = null;
        IsUnlocked = false;
    }

    /// <summary>Returns an error message, or null when the password is acceptable.</summary>
    public string? ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < LicenseCrypto.MinimumPasswordLength)
            return L.F("S_PwdTooShort", LicenseCrypto.MinimumPasswordLength);

        if (_keys.HasAnyPrivateKey() && !_keys.VerifyPassword(password))
            return L.T("S_PwdWrong");

        return null;
    }
}

public sealed class IssueRequest
{
    public string CustomerName { get; set; } = "";
    public string StationName { get; set; } = "";
    public DeviceBindingMode BindingMode { get; set; } = DeviceBindingMode.MachineFingerprint;
    public string DeviceId { get; set; } = "";
    public LicenseType LicenseType { get; set; } = LicenseType.Days90;
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }

    /// <summary>
    /// Optional expiry wall-clock time in InputTimeZone. Null means the end of ExpiryDate.
    /// Existing API callers default to UTC; the issuer UI explicitly supplies the local zone.
    /// </summary>
    public TimeOnly? ExpiryTime { get; set; }

    /// <summary>Input wall-clock zone. UTC default preserves existing API callers.</summary>
    public TimeZoneInfo InputTimeZone { get; set; } = TimeZoneInfo.Utc;

    public bool Perpetual { get; set; }
}

public sealed class IssuerService
{
    private readonly KeyStore _keys;
    private readonly LicenseRecordStore _records;

    public IssuerService(KeyStore keys, LicenseRecordStore records)
    {
        _keys = keys;
        _records = records;
    }

    public LicenseRecord Issue(ProductDefinition product, IssueRequest request, string password)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(request);

        if (!_keys.HasKeyPair(product.ProductId))
            throw new InvalidOperationException(L.F("S_SigningKeyMissing", product.ProductId));

        if (string.IsNullOrWhiteSpace(request.DeviceId))
            throw new InvalidOperationException(
                request.BindingMode == DeviceBindingMode.MachineFingerprint
                    ? L.T("S_IssuerPasteFingerprint")
                    : L.T("S_IssuerEnterDeviceId"));

        string device = request.DeviceId.Trim();
        if (!Enum.IsDefined(request.BindingMode))
            throw new InvalidOperationException(L.T("S_ErrDeviceRequired"));
        if (request.BindingMode == DeviceBindingMode.MacAddress && !MacAddress.TryNormalize(device, out device))
            throw new InvalidOperationException(L.T("S_ErrMacAddress"));
        if (request.BindingMode == DeviceBindingMode.MachineFingerprint &&
            (device.Length != 64 || !device.All(Uri.IsHexDigit)))
            throw new InvalidOperationException(L.T("S_ErrFingerprint"));

        var startUtc = LicenseDateInput.ToUtc(request.StartDate, TimeOnly.MinValue, request.InputTimeZone);

        var expiresUtc = request.Perpetual
            ? DateTimeOffset.MaxValue
            : LicenseDateInput.ExpiryUtc(request.ExpiryDate, request.ExpiryTime, request.InputTimeZone);

        if (!request.Perpetual && expiresUtc < startUtc)
            throw new InvalidOperationException(L.T("S_ErrExpiryOrder"));

        var payload = new LicensePayload
        {
            SchemaVersion = 1,
            LicenseId = CreateSerial(_records.Load().Select(r => r.LicenseId)),
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            CustomerName = request.CustomerName.Trim(),
            StationName = request.StationName.Trim(),
            DeviceId = device,
            BindingMode = request.BindingMode,
            LicenseType = request.Perpetual ? LicenseType.Perpetual : request.LicenseType,
            IssuedAtUtc = DateTimeOffset.UtcNow,
            NotBeforeUtc = startUtc,
            ExpiresAtUtc = expiresUtc
        };

        SignedLicense envelope = LicenseCrypto.Sign(
            payload,
            _keys.PrivateKeyPath(product.ProductId),
            password,
            product.Algorithm);

        string licenseText = LicenseCrypto.SerializeEnvelope(envelope);

        var record = new LicenseRecord
        {
            LicenseId = payload.LicenseId,
            ProductId = payload.ProductId,
            ProductName = payload.ProductName,
            CustomerName = payload.CustomerName,
            StationName = payload.StationName,
            DeviceId = payload.DeviceId,
            BindingMode = payload.BindingMode,
            LicenseType = payload.LicenseType,
            IssuedAtUtc = payload.IssuedAtUtc,
            NotBeforeUtc = payload.NotBeforeUtc,
            ExpiresAtUtc = payload.ExpiresAtUtc,
            KeyId = envelope.KeyId,
            Algorithm = envelope.Algorithm,
            LicenseText = licenseText
        };

        _records.Add(record);
        return record;
    }

    public static string CreateSerial(IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        int year = DateTime.UtcNow.Year;
        Span<char> suffix = stackalloc char[6];

        for (int attempt = 0; attempt < 100; attempt++)
        {
            for (int i = 0; i < suffix.Length; i++)
                suffix[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];

            string serial = $"WLC-{year}-{new string(suffix)}";
            if (!used.Contains(serial)) return serial;
        }

        return $"WLC-{year}-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
    }
}

public sealed class ExportService
{
    private readonly KeyStore _keys;

    public ExportService(KeyStore keys)
    {
        _keys = keys;
    }

    public string BuildPublicKeyBundle(IEnumerable<ProductDefinition> products)
    {
        var bundle = new PublicKeyBundle { GeneratedAtUtc = DateTimeOffset.UtcNow };

        foreach (var product in products)
        {
            string? pem = _keys.ReadPublicKey(product.ProductId);
            if (string.IsNullOrWhiteSpace(pem)) continue;

            bundle.Keys.Add(new PublicKeyEntry
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                KeyId = product.KeyId.Length > 0 ? product.KeyId : LicenseCrypto.ComputeKeyIdFromPublicPem(pem),
                Algorithm = product.AlgorithmLabel,
                PublicKeyPem = pem
            });
        }

        return PublicKeyBundleSerializer.Serialize(bundle);
    }

    /// <summary>
    /// Seals an issued license into an encrypted office.dat blob bound to the machine the
    /// license was issued for. Requires the vendor secret that ships inside the client app.
    /// </summary>
    public byte[] BuildOfficeFile(LicenseRecord record, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!LicenseCodec.TryParseLicenseText(record.LicenseText, out var envelope, out string error))
            throw new InvalidOperationException($"The stored license text is unreadable: {error}");

        string json = LicenseCrypto.SerializeEnvelope(envelope!);
        return OfficeLicenseFile.Create(json, record.ProductId, record.DeviceId.Trim(), secret);
    }

    public static string BuildCsv(IEnumerable<LicenseRecord> records)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("LicenseId,ProductId,ProductName,Customer,Station,DeviceId,Binding,Type,IssuedUtc,NotBeforeUtc,ExpiresUtc,Algorithm,KeyId,Revoked");

        foreach (var r in records.OrderByDescending(x => x.IssuedAtUtc))
        {
            sb.Append(Csv(r.LicenseId)).Append(',')
              .Append(Csv(r.ProductId)).Append(',')
              .Append(Csv(r.ProductName)).Append(',')
              .Append(Csv(r.CustomerName)).Append(',')
              .Append(Csv(r.StationName)).Append(',')
              .Append(Csv(r.DeviceId)).Append(',')
              .Append(Csv(r.BindingMode.ToString())).Append(',')
              .Append(Csv(r.LicenseType.ToString())).Append(',')
              .Append(Csv(r.IssuedAtUtc.ToString("O"))).Append(',')
              .Append(Csv(r.NotBeforeUtc.ToString("O"))).Append(',')
              .Append(Csv(r.IsPerpetual ? "Never" : r.ExpiresAtUtc.ToString("O"))).Append(',')
              .Append(Csv(r.Algorithm)).Append(',')
              .Append(Csv(r.KeyId)).Append(',')
              .Append(r.Revoked ? "Yes" : "No")
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        string text = value ?? "";
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
            return '"' + text.Replace("\"", "\"\"") + '"';
        return text;
    }
}
