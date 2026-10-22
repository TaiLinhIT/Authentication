using LicenseLocalization;
using LicenseShared;

namespace LicenseGenerator.Localization;

/// <summary>Short helpers for localized text in the issuer tool.</summary>
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

    public static string KeygenName(KeygenMode mode) =>
        mode == KeygenMode.ActivationCodes ? T("S_KeygenCodes") : T("S_KeygenSerial");

    public static string AlgorithmName(SigningAlgorithm algorithm) =>
        algorithm == SigningAlgorithm.Ecdsa ? T("S_AlgEcdsa") : T("S_AlgRsa");

    public static string Binding(DeviceBindingMode mode) =>
        mode == DeviceBindingMode.MachineFingerprint ? T("S_BindingFingerprint") : T("S_BindingDeviceId");

    public static string RecordState(LicenseRecordState state) => state switch
    {
        LicenseRecordState.Active => T("S_StatusActive"),
        LicenseRecordState.Perpetual => T("S_StatusPerpetual"),
        LicenseRecordState.Expiring => T("S_StatusExpiringSoon"),
        LicenseRecordState.Scheduled => T("S_StatusScheduled"),
        LicenseRecordState.Expired => T("S_StatusExpired"),
        LicenseRecordState.Revoked => T("S_StatusRevoked"),
        _ => state.ToString()
    };
}
