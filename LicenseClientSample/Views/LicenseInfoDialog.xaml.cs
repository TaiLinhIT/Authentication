using LicenseClientSample.Interop;
using LicenseClientSample.Localization;
using LicenseClientSample.Models;
using LicenseClientSample.Services;
using LicenseShared;
using System.Windows;

namespace LicenseClientSample.Views;

public partial class LicenseInfoDialog : Window
{
    public LicenseInfoDialog(ClientConfig config, LicenseValidator validator, LicenseValidationResult result)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        Title = L.T("S_LicenseInformation") + " — " + config.ProductName;

        var payload = result.Payload;
        if (payload is null)
        {
            LicenseIdValue.Text = L.T("S_NoLicenseInstalled");
            ProductValue.Text = config.ProductName;
            AlgorithmValue.Text = "—";
            FingerprintValue.Text = string.IsNullOrEmpty(validator.MachineFingerprint)
                ? L.T("S_Unavailable")
                : validator.MachineFingerprint;
            StorageValue.Text = new LicenseStore(config.ProductId).FilePath;
            return;
        }

        LicenseIdValue.Text = payload.LicenseId;
        ProductValue.Text = string.IsNullOrWhiteSpace(payload.ProductName)
            ? config.ProductName
            : $"{payload.ProductName}\n{payload.ProductId}";
        CustomerValue.Text = string.IsNullOrWhiteSpace(payload.CustomerName) ? "—" : payload.CustomerName;
        StationValue.Text = string.IsNullOrWhiteSpace(payload.StationName) ? "—" : payload.StationName;
        TypeValue.Text = L.TypeName(payload.LicenseType);
        IssuedValue.Text = payload.IssuedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ExpiresValue.Text = payload.IsPerpetual
            ? L.T("S_PerpetualNoExpiry")
            : payload.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        BindingValue.Text = L.Binding(payload.BindingMode);
        DeviceValue.Text = string.IsNullOrWhiteSpace(payload.DeviceId) ? "—" : payload.DeviceId;
        FingerprintValue.Text = string.IsNullOrEmpty(validator.MachineFingerprint)
            ? L.T("S_MachineIdentityReadError")
            : validator.MachineFingerprint;
        AlgorithmValue.Text = DescribeAlgorithm(config, validator);
        StorageValue.Text = new LicenseStore(config.ProductId).FilePath;
    }

    public bool WantsNewLicense { get; private set; }

    private static string DescribeAlgorithm(ClientConfig config, LicenseValidator validator)
    {
        try
        {
            string? text = new LicenseStore(config.ProductId).Load(validator.MachineFingerprint);
            if (string.IsNullOrWhiteSpace(text)) return L.T("S_VerifiedOffline");

            if (LicenseCodec.TryParseLicenseText(text, out var envelope, out _) && envelope is not null)
                return L.F("S_AlgorithmKey", envelope.Algorithm, ShortKeyId(envelope.KeyId));
        }
        catch (Exception)
        {
            // The dialog must never fail just because the descriptive metadata is missing.
        }

        return L.T("S_VerifiedOffline");
    }

    private static string ShortKeyId(string? keyId) =>
        string.IsNullOrWhiteSpace(keyId) ? "—" : keyId;

    private void CopyDevice_Click(object sender, RoutedEventArgs e)
    {
        string value = DeviceValue.Text;
        if (!string.IsNullOrWhiteSpace(value) && value != "—")
            Clipboard.SetText(value);
    }

    private void NewLicense_Click(object sender, RoutedEventArgs e)
    {
        WantsNewLicense = true;
        DialogResult = true;
    }
}
