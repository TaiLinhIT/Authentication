using LicenseClientSample.Interop;
using LicenseClientSample.Localization;
using LicenseClientSample.Models;
using LicenseClientSample.Services;
using LicenseLocalization;
using LicenseShared;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace LicenseClientSample.Views;

public partial class ActivationWindow : Window
{
    private enum WarningKind
    {
        None,
        MachineIdentity,
        PublicKeys
    }

    private readonly ClientConfig _config;
    private readonly LicenseValidator _validator;
    private readonly LicenseValidationResult _current;
    private readonly string? _noticeKey;
    private readonly WarningKind _warning;
    private Func<string>? _errorProvider;
    private bool _keyVisible;

    public ActivationWindow(ClientConfig config, LicenseValidator validator, LicenseValidationResult current,
        string? noticeKey = null)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        _config = config;
        _validator = validator;
        _current = current;
        _noticeKey = noticeKey;

        if (!validator.HasMachineIdentity) _warning = WarningKind.MachineIdentity;
        else if (validator.PublicKeys.IsEmpty) _warning = WarningKind.PublicKeys;

        if (_warning != WarningKind.None) ActivateButton.IsEnabled = false;

        ApplyLanguage();

        KeyPassword.PasswordChanged += (_, _) =>
        {
            _errorProvider = null;
            ErrorBorder.Visibility = Visibility.Collapsed;
        };

        LanguageManager.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LanguageManager.LanguageChanged -= OnLanguageChanged;

        Loaded += (_, _) => KeyPassword.Focus();
    }

    public LicenseValidationResult? ActivatedLicense { get; private set; }

    private string CurrentKey => _keyVisible ? KeyPlain.Text : KeyPassword.Password;

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        Title = L.F("S_ActivationWindowTitle", _config.ProductName);
        ProductLine.Text = $"{_config.ProductName} · {_config.Vendor}";
        VersionText.Text = "v" + _config.Version;
        VendorText.Text = $"{_config.Vendor} · {_config.ProductName} v{_config.Version}";
        IntroText.Text = L.F("S_ActivationIntro", _config.Vendor, _config.ProductName);

        DeviceIdText.Text = string.IsNullOrEmpty(_validator.MachineDeviceId)
            ? L.T("S_Unavailable")
            : _validator.MachineDeviceId;
        FingerprintText.Text = string.IsNullOrEmpty(_validator.MachineFingerprint)
            ? L.T("S_MachineIdentityReadError")
            : _validator.MachineFingerprint;

        KeyWarningBorder.Visibility = _warning == WarningKind.None ? Visibility.Collapsed : Visibility.Visible;
        KeyWarningText.Text = _warning switch
        {
            WarningKind.MachineIdentity => L.T("S_MachineIdentityUnavailable"),
            WarningKind.PublicKeys => L.T("S_KeyMissingWarning"),
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(_noticeKey))
        {
            ReasonText.Text = L.T(_noticeKey);
            ReasonBorder.Visibility = Visibility.Visible;
        }
        else if (_current.Status != LicenseStatus.Missing && !string.IsNullOrWhiteSpace(_current.Message))
        {
            ReasonText.Text = L.Describe(_current, _config.ProductId);
            ReasonBorder.Visibility = Visibility.Visible;
        }

        UpdateShowKeyButton();

        if (_errorProvider is not null)
        {
            ErrorText.Text = _errorProvider();
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }

    private void UpdateShowKeyButton() =>
        ShowKeyButton.Content = _keyVisible ? L.T("S_HideKey") : L.T("S_ShowKey");

    private void SetKey(string text)
    {
        KeyPassword.Password = text;
        KeyPlain.Text = text;
        _errorProvider = null;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowError(Func<string> messageProvider)
    {
        _errorProvider = messageProvider;
        ErrorText.Text = messageProvider();
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void Language_Click(object sender, RoutedEventArgs e) => LanguageManager.Toggle();

    private void ToggleKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;

        if (_keyVisible)
        {
            KeyPlain.Text = KeyPassword.Password;
            KeyPassword.Visibility = Visibility.Collapsed;
            KeyPlain.Visibility = Visibility.Visible;
            KeyPlain.Focus();
            KeyPlain.CaretIndex = KeyPlain.Text.Length;
        }
        else
        {
            KeyPassword.Password = KeyPlain.Text;
            KeyPlain.Visibility = Visibility.Collapsed;
            KeyPassword.Visibility = Visibility.Visible;
            KeyPassword.Focus();
        }

        UpdateShowKeyButton();
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                SetKey(Clipboard.GetText().Trim());
                if (!_keyVisible) KeyPassword.Focus();
            }
            else
            {
                ShowError(() => L.T("S_ClipboardEmpty"));
            }
        }
        catch (Exception)
        {
            ShowError(() => L.T("S_ClipboardBusy"));
        }
    }

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("S_SelectLicenseFile"),
            Filter = L.T("S_FilterLicenseFile")
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetKey(File.ReadAllText(dialog.FileName).Trim());
        }
        catch (Exception ex)
        {
            ShowError(() => L.F("S_ReadFileError", ex.Message));
        }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
        _errorProvider = null;
        string key = CurrentKey.Trim();

        if (key.Length == 0)
        {
            ShowError(() => L.T("S_EnterKeyFirst"));
            return;
        }

        ActivateButton.IsEnabled = false;

        try
        {
            var result = _validator.Activate(key, DateTimeOffset.UtcNow);
            if (!result.IsValid)
            {
                ShowError(() => L.Describe(result, _config.ProductId));
                ActivateButton.IsEnabled = true;
                return;
            }

            ActivatedLicense = result;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(() => L.F("S_ActivationFailed", ex.Message));
            ActivateButton.IsEnabled = true;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CopyDeviceId_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_validator.MachineDeviceId))
            Clipboard.SetText(_validator.MachineDeviceId);
    }

    private void CopyFingerprint_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_validator.MachineFingerprint))
            Clipboard.SetText(_validator.MachineFingerprint);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            try
            {
                SetKey(File.ReadAllText(files[0]).Trim());
            }
            catch (Exception ex)
            {
                ShowError(() => L.F("S_DroppedFileError", ex.Message));
            }
        }
    }
}
