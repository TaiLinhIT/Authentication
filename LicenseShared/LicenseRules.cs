using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicenseShared;

/// <summary>Business rules applied after the signature is verified.</summary>
public static class LicenseRules
{
    public static TimeSpan NotBeforeTolerance { get; set; } = TimeSpan.FromMinutes(5);

    public static LicenseValidationResult Evaluate(
        LicensePayload payload,
        string expectedProductId,
        string currentFingerprint,
        string currentDeviceId,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!string.Equals(payload.ProductId, expectedProductId, StringComparison.OrdinalIgnoreCase))
            return LicenseValidationResult.Fail(
                LicenseStatus.WrongProduct,
                $"This license belongs to '{payload.ProductId}', not '{expectedProductId}'.");

        string expectedDevice = payload.BindingMode == DeviceBindingMode.ManualDeviceId
            ? currentDeviceId
            : currentFingerprint;

        if (!string.Equals(payload.DeviceId, expectedDevice, StringComparison.OrdinalIgnoreCase))
            return LicenseValidationResult.Fail(
                LicenseStatus.WrongMachine,
                payload.BindingMode == DeviceBindingMode.ManualDeviceId
                    ? "This license is registered to a different device ID."
                    : "This license is bound to a different machine fingerprint.",
                payload);

        if (nowUtc < payload.NotBeforeUtc - NotBeforeTolerance)
            return LicenseValidationResult.Fail(
                LicenseStatus.NotYetValid,
                $"This license becomes active on {payload.NotBeforeUtc:yyyy-MM-dd HH:mm} (UTC).",
                payload);

        if (!payload.IsPerpetual && nowUtc > payload.ExpiresAtUtc)
            return LicenseValidationResult.Fail(
                LicenseStatus.Expired,
                $"This license expired on {payload.ExpiresAtUtc:yyyy-MM-dd HH:mm} (UTC).",
                payload);

        return LicenseValidationResult.Valid(payload);
    }
}

public enum LicenseRecordState
{
    Active,
    Perpetual,
    Expiring,
    Scheduled,
    Expired,
    Revoked
}

public static class LicenseRecordStatus
{
    public static LicenseRecordState Evaluate(LicenseRecord record, DateTimeOffset nowUtc)
    {
        if (record.Revoked) return LicenseRecordState.Revoked;
        if (nowUtc < record.NotBeforeUtc) return LicenseRecordState.Scheduled;
        if (record.LicenseType == LicenseType.Perpetual) return LicenseRecordState.Perpetual;
        if (nowUtc > record.ExpiresAtUtc) return LicenseRecordState.Expired;
        if (record.ExpiresAtUtc - nowUtc <= TimeSpan.FromDays(30)) return LicenseRecordState.Expiring;
        return LicenseRecordState.Active;
    }

    public static string Label(LicenseRecordState state) => state switch
    {
        LicenseRecordState.Active => "Active",
        LicenseRecordState.Perpetual => "Perpetual",
        LicenseRecordState.Expiring => "Expiring soon",
        LicenseRecordState.Scheduled => "Scheduled",
        LicenseRecordState.Expired => "Expired",
        LicenseRecordState.Revoked => "Revoked",
        _ => "Unknown"
    };
}

public static class PublicKeyBundleSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize(PublicKeyBundle bundle) => JsonSerializer.Serialize(bundle, Options);

    public static PublicKeyBundle? Deserialize(string json)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<PublicKeyBundle>(json, Options);
            if (bundle?.Keys is null) return null;

            bundle.Keys = bundle.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k.KeyId) && !string.IsNullOrWhiteSpace(k.PublicKeyPem))
                .ToList();

            return bundle;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
