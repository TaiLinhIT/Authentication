using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicenseShared;

/// <summary>Business rules applied after the signature is verified.</summary>
public static class LicenseRules
{
    public static TimeSpan NotBeforeTolerance { get; set; } = TimeSpan.FromMinutes(5);

    // Keep the original signature for clients already compiled against this assembly.
    public static LicenseValidationResult Evaluate(LicensePayload payload, string expectedProductId,
        string currentFingerprint, string currentDeviceId, DateTimeOffset nowUtc) =>
        Evaluate(payload, expectedProductId, currentFingerprint, currentDeviceId, nowUtc, null);

    public static LicenseValidationResult Evaluate(
        LicensePayload payload,
        string expectedProductId,
        string currentFingerprint,
        string currentDeviceId,
        DateTimeOffset nowUtc,
        IEnumerable<string>? currentMacAddresses)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!string.Equals(payload.ProductId, expectedProductId, StringComparison.OrdinalIgnoreCase))
            return LicenseValidationResult.Fail(
                LicenseStatus.WrongProduct,
                $"This license belongs to '{payload.ProductId}', not '{expectedProductId}'.");

        if (!Enum.IsDefined(payload.BindingMode))
            return LicenseValidationResult.Fail(LicenseStatus.Malformed, "Unsupported device binding mode.", payload);

        var addresses = payload.BindingMode == DeviceBindingMode.MacAddress
            ? (currentMacAddresses ?? MacAddress.GetCurrent()).ToArray() : Array.Empty<string>();
        if (payload.BindingMode == DeviceBindingMode.MacAddress && addresses.Length == 0)
            return LicenseValidationResult.Fail(LicenseStatus.DeviceIdentityUnavailable,
                "No network adapter MAC address is available.", payload);

        if (!MatchesDevice(payload, currentFingerprint, currentDeviceId, addresses))
            return LicenseValidationResult.Fail(
                LicenseStatus.WrongMachine,
                payload.BindingMode == DeviceBindingMode.MacAddress
                    ? "This license is bound to a different MAC address."
                    : payload.BindingMode == DeviceBindingMode.ManualDeviceId
                    ? "This license is registered to a different device ID."
                    : "This license is bound to a different machine fingerprint.",
                payload);

        if (nowUtc < payload.NotBeforeUtc - NotBeforeTolerance)
            return LicenseValidationResult.Fail(
                LicenseStatus.NotYetValid,
                $"This license becomes active on {payload.NotBeforeUtc:yyyy-MM-dd HH:mm} (UTC).",
                payload);

        if (!payload.IsPerpetual && nowUtc > payload.ExpiresAtUtc && !ExtraExperience.Allows(payload.ExtraWindows, nowUtc))
            return LicenseValidationResult.Fail(
                LicenseStatus.Expired,
                $"This license expired on {payload.ExpiresAtUtc:yyyy-MM-dd HH:mm} (UTC).",
                payload);

        return LicenseValidationResult.Valid(payload);
    }

    public static bool MatchesDevice(LicensePayload payload, string fingerprint, string deviceId,
        IEnumerable<string>? macAddresses = null) => payload.BindingMode switch
    {
        DeviceBindingMode.MachineFingerprint => !string.IsNullOrWhiteSpace(payload.DeviceId)
            && string.Equals(payload.DeviceId, fingerprint, StringComparison.OrdinalIgnoreCase),
        DeviceBindingMode.ManualDeviceId => !string.IsNullOrWhiteSpace(payload.DeviceId)
            && string.Equals(payload.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase),
        DeviceBindingMode.MacAddress => MacAddress.Matches(payload.DeviceId, macAddresses ?? MacAddress.GetCurrent()),
        _ => false
    };
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
        if (nowUtc > record.ExpiresAtUtc)
        {
            if (ExtraExperience.Allows(record.ExtraWindows, nowUtc)) return LicenseRecordState.Active;
            if (record.ExtraWindows.Any(w => w.StartUtc > nowUtc)) return LicenseRecordState.Scheduled;
            return LicenseRecordState.Expired;
        }
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
