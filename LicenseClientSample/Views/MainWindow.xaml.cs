using LicenseClientSample.Interop;
using LicenseClientSample.Localization;
using LicenseClientSample.Models;
using LicenseClientSample.Services;
using LicenseLocalization;
using LicenseShared;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LicenseClientSample.Views;

public partial class MainWindow : Window
{
    private readonly ClientConfig _config;
    private readonly LicenseValidator _validator;
    private readonly DispatcherTimer _timer;
    private LicenseValidationResult _current;

    private DateTime? _lastCheckLocal;
    private bool _lastCheckManual;
    private string? _compareResultKey;
    private string _compareKind = "";

    public MainWindow(ClientConfig config, LicenseValidator validator, LicenseValidationResult current)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        _config = config;
        _validator = validator;
        _current = current;

        Title = config.ProductName;
        ProductNameText.Text = config.ProductName;
        VendorText.Text = config.Vendor;
        VersionText.Text = "v" + config.Version;

        _lastCheckLocal = DateTime.Now;
        UpdateLicense(current);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += (_, _) => Revalidate(manual: false);
        _timer.Start();

        LanguageManager.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            LanguageManager.LanguageChanged -= OnLanguageChanged;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        LicensePill.Content = L.T("S_LicenseActive");
        UpdateLicenseText();
        ReapplyCompareResult();
    }

    private void UpdateLicense(LicenseValidationResult result)
    {
        _current = result;
        _lastCheckLocal = DateTime.Now;
        UpdateLicenseText();
    }

    private void UpdateLicenseText()
    {
        var payload = _current.Payload;
        if (payload is null) return;

        LicensedToText.Text = string.IsNullOrWhiteSpace(payload.CustomerName)
            ? L.F("S_LicensedTo", Fallback(payload.StationName))
            : L.F("S_LicensedTo", payload.CustomerName);

        StationValue.Text = Fallback(payload.StationName);
        CustomerValue.Text = Fallback(payload.CustomerName);
        ExpiryValue.Text = payload.IsPerpetual
            ? L.T("S_PerpetualLicense")
            : payload.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd");

        if (payload.IsPerpetual)
        {
            RemainingValue.Text = L.T("S_NoExpiry");
        }
        else
        {
            double days = (payload.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalDays;
            RemainingValue.Text = days <= 0
                ? L.T("S_ExpiredLabel")
                : L.F("S_DaysRemaining", Math.Ceiling(days).ToString("0"));
        }

        DeviceValue.Text = string.IsNullOrEmpty(_validator.MachineDeviceId)
            ? L.T("S_Unavailable")
            : _validator.MachineDeviceId;

        LastCheckValue.Text = (_lastCheckLocal ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm")
                              + (_lastCheckManual ? " " + L.T("S_JustNow") : "");

        LicensePill.Content = L.T("S_LicenseActive");
        LicensePill.Tag = "success";
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void Language_Click(object sender, RoutedEventArgs e) => LanguageManager.Toggle();

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        string a = LabelA.Text.Trim();
        string b = LabelB.Text.Trim();

        if (a.Length == 0 || b.Length == 0)
        {
            SetCompareResult("S_CompareEmpty", "warning");
            return;
        }

        if (string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase))
        {
            SetCompareResult("S_CompareMatch", "success");
            return;
        }

        SetCompareResult("S_CompareDifferent", "danger");
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private void SetCompareResult(string key, string kind)
    {
        _compareResultKey = key;
        _compareKind = kind;

        CompareResult.Text = L.T(key);
        CompareResult.Foreground = (Brush)FindResource(kind switch
        {
            "success" => "SuccessBrush",
            "danger" => "DangerBrush",
            "warning" => "WarningBrush",
            _ => "TextPrimaryBrush"
        });

        CompareResultBorder.Background = (Brush)FindResource(kind switch
        {
            "success" => "SuccessSoftBrush",
            "danger" => "DangerSoftBrush",
            "warning" => "WarningSoftBrush",
            _ => "CardAltBrush"
        });
    }

    private void ReapplyCompareResult()
    {
        if (_compareResultKey is not null) SetCompareResult(_compareResultKey, _compareKind);
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => Revalidate(manual: true);

    private void Revalidate(bool manual)
    {
        LicenseValidationResult result;
        try
        {
            result = _validator.ValidateInstalled(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("S_LicenseCheckFailed", ex.Message),
                L.T("S_LicenseCheckTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result.IsValid)
        {
            _lastCheckManual = manual;
            UpdateLicense(result);
            return;
        }

        _timer.Stop();

        var activation = new ActivationWindow(_config, _validator, result) { Owner = this };
        if (activation.ShowDialog() == true && activation.ActivatedLicense is not null)
        {
            _lastCheckManual = false;
            UpdateLicense(activation.ActivatedLicense);
            _timer.Start();
            return;
        }

        Close();
    }

    private void License_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LicenseInfoDialog(_config, _validator, _current) { Owner = this };
        dialog.ShowDialog();

        if (dialog.WantsNewLicense)
            EnterNewLicense();
    }

    /// <summary>Opens activation even though the installed license is still valid (renewal/replacement).</summary>
    private void EnterNewLicense()
    {
        _timer.Stop();

        LicenseValidationResult current;
        try
        {
            current = _validator.ValidateInstalled(DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            current = _current;
        }

        var activation = new ActivationWindow(_config, _validator, current, "S_ReplacementNotice")
        {
            Owner = this
        };

        if (activation.ShowDialog() == true && activation.ActivatedLicense is not null)
        {
            _lastCheckManual = false;
            UpdateLicense(activation.ActivatedLicense);
            _timer.Start();
            return;
        }

        if (current.IsValid)
        {
            _timer.Start();
            return;
        }

        Close();
    }
}
