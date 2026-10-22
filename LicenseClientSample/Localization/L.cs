using LicenseLocalization;
using LicenseShared;

namespace LicenseClientSample.Localization;

/// <summary>Short helpers for localized text in the client.</summary>
public static class L
{
    public static string T(string key) => LanguageManager.T(key);

    public static string F(string key, params object?[] args) => LanguageManager.Format(key, args);

    public static string TypeName(LicenseType type) => type switch
    {
        LicenseType.Trial => T("S_TypeTrial"),
        LicenseType.Days30 => T("S_TypeDays30"),
        LicenseType.Days90 => T("S_TypeDays90"),
        LicenseType.OneYear => T("S_TypeOneYear"),
        LicenseType.Perpetual => T("S_TypePerpetual"),
        _ => T("S_TypeCustom")
    };

    public static string Binding(DeviceBindingMode mode) =>
        mode == DeviceBindingMode.MachineFingerprint ? T("S_BindingFingerprint") : T("S_BindingDeviceId");

    /// <summary>Turns a validation result into a message in the active language.</summary>
    public static string Describe(LicenseValidationResult result, string expectedProductId)
    {
        var payload = result.Payload;

        return result.Status switch
        {
            LicenseStatus.Missing => T("S_NoLicenseInstalledMessage"),
            LicenseStatus.Malformed => T("S_LicenseInvalid"),
            LicenseStatus.UnknownKey => T("S_UnknownKey"),
            LicenseStatus.InvalidSignature => T("S_InvalidSignatureMessage"),
            LicenseStatus.WrongProduct => payload is null
                ? T("S_LicenseInvalid")
                : F("S_WrongProductMessage", payload.ProductId, expectedProductId),
            LicenseStatus.WrongMachine => payload?.BindingMode == DeviceBindingMode.ManualDeviceId
                ? T("S_WrongMachineIdMessage")
                : T("S_WrongMachineFpMessage"),
            LicenseStatus.NotYetValid => payload is null
                ? T("S_LicenseInvalid")
                : F("S_NotYetValidMessage", payload.NotBeforeUtc.ToString("yyyy-MM-dd HH:mm")),
            LicenseStatus.Expired => payload is null
                ? T("S_LicenseInvalid")
                : F("S_ExpiredMessage", payload.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm")),
            LicenseStatus.ClockRollback => T("S_ClockRollbackMessage"),
            LicenseStatus.DeviceIdentityUnavailable => T("S_DeviceIdentityUnavailableMessage"),
            LicenseStatus.Valid => T("S_LicenseIsValid"),
            _ => result.Message
        };
    }
}
