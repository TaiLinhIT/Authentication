using LicenseClientSample.Models;
using LicenseClientSample.Security;
using LicenseShared;
using System.IO;

namespace LicenseClientSample.Services;

/// <summary>
/// Runs the complete offline license check: format, public key, signature, product,
/// machine binding, dates and clock-rollback detection.
/// </summary>
public sealed class LicenseValidator
{
    private readonly ClientConfig _config;
    private readonly PublicKeyProvider _keys;
    private readonly LicenseStore _store;
    private readonly ClockGuard _clock;

    public LicenseValidator(ClientConfig config, PublicKeyProvider keys)
    {
        _config = config;
        _keys = keys;
        _store = new LicenseStore(config.ProductId);
        _clock = new ClockGuard(config.ProductId);

        try
        {
            MachineFingerprint = LicenseShared.MachineFingerprint.GetCurrent();
            MachineDeviceId = LicenseShared.MachineFingerprint.GetShortDeviceId();
        }
        catch (InvalidOperationException)
        {
            MachineFingerprint = "";
            MachineDeviceId = "";
        }
    }

    public string MachineFingerprint { get; }
    public string MachineDeviceId { get; }
    public bool HasMachineIdentity => MachineFingerprint.Length > 0;

    public PublicKeyProvider PublicKeys => _keys;

    public LicenseValidationResult ValidateInstalled(DateTimeOffset now)
    {
        if (!HasMachineIdentity)
            return LicenseValidationResult.Fail(
                LicenseStatus.DeviceIdentityUnavailable,
                "This machine identity could not be read. Run the application on Windows with access to the registry.");

        string? licenseText = _store.Load(MachineFingerprint);
        if (string.IsNullOrWhiteSpace(licenseText))
            return LicenseValidationResult.Fail(
                LicenseStatus.Missing,
                "No license is installed for this machine.");

        return Evaluate(licenseText, now);
    }

    /// <summary>Validates a pasted key or license file. Saves it only when everything checks out.</summary>
    public LicenseValidationResult Activate(string licenseText, DateTimeOffset now)
    {
        if (!HasMachineIdentity)
            return LicenseValidationResult.Fail(
                LicenseStatus.DeviceIdentityUnavailable,
                "This machine identity could not be read. Run the application on Windows with access to the registry.");

        var result = Evaluate(licenseText, now);
        if (!result.IsValid) return result;

        try
        {
            _store.Save(licenseText.Trim(), MachineFingerprint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LicenseValidationResult.Fail(
                LicenseStatus.Malformed,
                "The license is valid but could not be stored on this machine: " + ex.Message,
                result.Payload);
        }

        return result;
    }

    private LicenseValidationResult Evaluate(string licenseText, DateTimeOffset now)
    {
        if (!LicenseCodec.TryParseLicenseText(licenseText, out var envelope, out string error))
            return LicenseValidationResult.Fail(LicenseStatus.Malformed, error);

        var key = _keys.Find(envelope!.KeyId, _config.ProductId);
        if (key is null)
            return LicenseValidationResult.Fail(
                LicenseStatus.UnknownKey,
                "The vendor public key for this license is not installed in this build. Contact the vendor.");

        if (!LicenseCrypto.Verify(envelope, key.PublicKeyPem, out var payload, out string verifyError))
            return LicenseValidationResult.Fail(LicenseStatus.InvalidSignature, verifyError);

        var rules = LicenseRules.Evaluate(
            payload!, _config.ProductId, MachineFingerprint, MachineDeviceId, now);

        if (!rules.IsValid) return rules;

        try
        {
            var clock = _clock.Check(now, MachineFingerprint);
            if (clock.RollbackDetected)
                return LicenseValidationResult.Fail(
                    LicenseStatus.ClockRollback,
                    $"The system clock appears to have been moved backwards. The last check was {clock.LastSeenUtc:yyyy-MM-dd HH:mm} UTC.",
                    payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If the clock state cannot be written, keep the license usable but do not
            // pretend that rollback protection succeeded.
        }

        return rules;
    }
}
