using LicenseGenerator.Interop;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Windows;
using System.Windows.Media;

namespace LicenseGenerator.Views.Dialogs;

public partial class LicenseDetailsDialog : Window
{
    private readonly AppServices _services;
    private readonly string _licenseText;
    private string _activationCode = "";

    public LicenseDetailsDialog(AppServices services, string licenseText)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        _services = services;
        _licenseText = licenseText;

        Loaded += (_, _) => Inspect();
    }

    private void Inspect()
    {
        if (!LicenseCodec.TryParseLicenseText(_licenseText, out var envelope, out string error))
        {
            SetPill(L.T("S_Unreadable"), "danger");
            SignatureNote.Text = error;
            SignatureNote.Foreground = (Brush)FindResource("DangerBrush");
            SerialText.Text = L.T("S_UnrecognizedLicense");
            CodeValue.Text = _licenseText;
            return;
        }

        try
        {
            _activationCode = LicenseCodec.CreateActivationCode(envelope!);
            CodeValue.Text = _activationCode;
        }
        catch (Exception ex)
        {
            CodeValue.Text = L.F("S_CouldNotRebuildCode", ex.Message);
        }

        LicensePayload? payload = null;
        try
        {
            payload = LicenseCodec.DeserializePayload(Convert.FromBase64String(envelope!.PayloadBase64));
        }
        catch (FormatException)
        {
            SetPill(L.T("S_Malformed"), "danger");
            SignatureNote.Text = L.T("S_SignatureNoteMalformed");
            return;
        }

        SerialText.Text = payload.LicenseId;
        ProductValue.Text = $"{payload.ProductName}\n{payload.ProductId}";
        CustomerValue.Text = string.IsNullOrWhiteSpace(payload.CustomerName) ? "—" : payload.CustomerName;
        StationValue.Text = string.IsNullOrWhiteSpace(payload.StationName) ? "—" : payload.StationName;
        BindingValue.Text = L.Binding(payload.BindingMode);
        DeviceValue.Text = payload.DeviceId;
        TypeValue.Text = L.TypeName(payload.LicenseType);
        IssuedValue.Text = payload.IssuedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ExpiresValue.Text = payload.IsPerpetual ? L.T("S_Never") : LicenseFormatting.Expiry(payload.ExpiresAtUtc);
        AlgorithmValue.Text = envelope!.Algorithm;
        KeyIdValue.Text = envelope.KeyId;

        string? publicKey = FindPublicKey(envelope);
        if (publicKey is null)
        {
            SetPill(L.T("S_PublicKeyMissing"), "warning");
            SignatureNote.Text = L.T("S_SignatureNoteMissingKey");
            SignatureNote.Foreground = (Brush)FindResource("WarningBrush");
        }
        else if (LicenseCrypto.Verify(envelope, publicKey, out _, out string verifyError))
        {
            SetPill(L.T("S_SignatureValid"), "success");
            SignatureNote.Text = L.T("S_SignatureNoteValid");
            SignatureNote.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            SetPill(L.T("S_InvalidSignaturePill"), "danger");
            SignatureNote.Text = verifyError;
            SignatureNote.Foreground = (Brush)FindResource("DangerBrush");
        }

        var now = DateTimeOffset.UtcNow;
        string status;
        if (now < payload.NotBeforeUtc)
            status = L.F("S_StatusCodeNotActive", payload.NotBeforeUtc.ToString("yyyy-MM-dd HH:mm"));
        else if (!payload.IsPerpetual && now > payload.ExpiresAtUtc)
            status = L.F("S_StatusCodeExpired", payload.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"));
        else
            status = payload.IsPerpetual
                ? L.T("S_StatusCodeActivePerpetual")
                : L.F("S_StatusCodeActiveUntil", payload.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"));

        try
        {
            string fingerprint = MachineFingerprint.GetCurrent();
            bool matches = LicenseRules.MatchesDevice(payload, fingerprint, MachineFingerprint.GetShortDeviceId());

            status += matches ? " " + L.T("S_BindingMatches") : " " + L.T("S_BindingMismatch");
        }
        catch (InvalidOperationException)
        {
            // Fingerprint unavailable; keep the date-based status.
        }

        StatusValue.Text = status;
    }

    private string? FindPublicKey(SignedLicense envelope)
    {
        foreach (var product in _services.Products.Load())
        {
            string? pem = _services.Keys.ReadPublicKey(product.ProductId);
            if (pem is null) continue;

            try
            {
                if (string.Equals(LicenseCrypto.ComputeKeyIdFromPublicPem(pem), envelope.KeyId, StringComparison.OrdinalIgnoreCase))
                    return pem;
            }
            catch (FormatException)
            {
                // Skip damaged key files.
            }
        }

        return null;
    }

    private void SetPill(string text, string kind)
    {
        SignaturePill.Content = text;
        SignaturePill.Tag = kind;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        _services.Dialogs.CopyToClipboard(string.IsNullOrWhiteSpace(_activationCode) ? _licenseText : _activationCode);
        SignatureNote.Text = L.T("S_CopiedToClipboard");
    }
}
