using System.Text.Json.Serialization;

namespace LicenseShared;

/// <summary>Signing algorithm family used by a product key.</summary>
public enum SigningAlgorithm
{
    Rsa,
    Ecdsa
}

/// <summary>How the issuer delivers a generated key.</summary>
public enum KeygenMode
{
    SerialNumbers,
    ActivationCodes
}

public enum LicenseType
{
    Trial,
    Days30,
    Days90,
    OneYear,
    Perpetual,
    Custom
}

public enum DeviceBindingMode
{
    /// <summary>Payload carries the full machine fingerprint hash.</summary>
    MachineFingerprint,

    /// <summary>Payload carries the short device id displayed by the client.</summary>
    ManualDeviceId,

    /// <summary>Payload carries a normalized 48-bit network adapter MAC address.</summary>
    MacAddress
}

public enum LicenseStatus
{
    Valid,
    Missing,
    Malformed,
    UnknownKey,
    InvalidSignature,
    WrongProduct,
    WrongMachine,
    NotYetValid,
    Expired,
    ClockRollback,
    DeviceIdentityUnavailable
}

/// <summary>A product registered by the issuer. The key pair is created once per product.</summary>
public sealed class ProductDefinition
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public KeygenMode KeygenMode { get; set; } = KeygenMode.SerialNumbers;
    public SigningAlgorithm Algorithm { get; set; } = SigningAlgorithm.Rsa;

    /// <summary>RSA key size in bits (2048/3072/4096) or EC curve size (256/384).</summary>
    public int KeySize { get; set; } = 3072;

    public string KeyId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Notes { get; set; } = "";

    [JsonIgnore]
    public string AlgorithmLabel => LicenseCrypto.AlgorithmLabel(Algorithm, KeySize);

    [JsonIgnore]
    public string KeygenModeLabel => KeygenMode == KeygenMode.SerialNumbers ? "Serial Numbers" : "Activation Codes";

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var parts = ProductName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Length <= 2 ? parts[0].ToUpperInvariant() : parts[0][..2].ToUpperInvariant();
            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
        }
    }
}

/// <summary>Signed content of a license. This is what the client trusts after signature verification.</summary>
public sealed class LicensePayload
{
    public int SchemaVersion { get; set; } = 1;
    public string LicenseId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string StationName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DeviceBindingMode BindingMode { get; set; } = DeviceBindingMode.MachineFingerprint;
    public LicenseType LicenseType { get; set; } = LicenseType.Days90;
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string KeyId { get; set; } = "";

    [JsonIgnore]
    public bool IsPerpetual => LicenseType == LicenseType.Perpetual;
}

/// <summary>License envelope: the signed payload plus the signature and key metadata.</summary>
public sealed class SignedLicense
{
    public string Format { get; set; } = LicenseCrypto.Format;
    public string Algorithm { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PayloadBase64 { get; set; } = "";
    public string SignatureBase64 { get; set; } = "";
}

public sealed record LicenseValidationResult(LicenseStatus Status, string Message, LicensePayload? Payload = null)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    public static LicenseValidationResult Valid(LicensePayload payload) =>
        new(LicenseStatus.Valid, "License is valid.", payload);

    public static LicenseValidationResult Fail(LicenseStatus status, string message, LicensePayload? payload = null) =>
        new(status, message, payload);
}

/// <summary>Issuer-side record of a license that was generated. Contains no secrets.</summary>
public sealed class LicenseRecord
{
    public string LicenseId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string StationName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DeviceBindingMode BindingMode { get; set; }
    public LicenseType LicenseType { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string KeyId { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public string LicenseText { get; set; } = "";
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public bool Ticketed { get; set; }

    [JsonIgnore]
    public bool IsPerpetual => LicenseType == LicenseType.Perpetual;
}

/// <summary>Bundle of public keys shipped with (or installed next to) the client application.</summary>
public sealed class PublicKeyBundle
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<PublicKeyEntry> Keys { get; set; } = new();
}

public sealed class PublicKeyEntry
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
}

public static class LicenseDurations
{
    public static TimeSpan? Duration(LicenseType type) => type switch
    {
        LicenseType.Trial => TimeSpan.FromDays(14),
        LicenseType.Days30 => TimeSpan.FromDays(30),
        LicenseType.Days90 => TimeSpan.FromDays(90),
        LicenseType.OneYear => TimeSpan.FromDays(365),
        _ => null
    };

    public static string Label(LicenseType type) => type switch
    {
        LicenseType.Trial => "Trial (14 days)",
        LicenseType.Days30 => "30 days",
        LicenseType.Days90 => "90 days",
        LicenseType.OneYear => "1 year",
        LicenseType.Perpetual => "Perpetual",
        _ => "Custom"
    };

    /// <summary>Expiry date (inclusive) for a duration-based license, starting on <paramref name="startDate"/>.</summary>
    public static DateOnly? DefaultExpiryDate(LicenseType type, DateOnly startDate)
    {
        var duration = Duration(type);
        return duration is null ? null : startDate.AddDays((int)duration.Value.TotalDays - 1);
    }

    /// <summary>End-of-day UTC instant for an inclusive expiry date.</summary>
    public static DateTimeOffset EndOfDayUtc(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddDays(1).AddTicks(-1);

    /// <summary>
    /// UTC instant at which the license expires. When <paramref name="time"/> is null the license
    /// stays valid through the end of the expiry date (23:59:59 UTC); otherwise it expires at the
    /// exact minute entered.
    /// </summary>
    public static DateTimeOffset ExpiryInstantUtc(DateOnly date, TimeOnly? time) =>
        time is null ? EndOfDayUtc(date) : new DateTimeOffset(date.ToDateTime(time.Value, DateTimeKind.Utc));
}
