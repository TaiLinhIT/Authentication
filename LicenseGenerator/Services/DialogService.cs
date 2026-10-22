using LicenseGenerator.Localization;
using LicenseGenerator.Views.Dialogs;
using LicenseShared;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;

namespace LicenseGenerator.Services;

public sealed class DialogService : IDialogService
{
    private readonly AppServices _services;

    public DialogService(AppServices services)
    {
        _services = services;
    }

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public ProductDefinition? ShowAddProduct(ProductDefinition? existing = null)
    {
        var dialog = new AddProductDialog(_services, existing);
        if (Owner is { } owner && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool ShowIssueLicense(ProductDefinition? product = null, LicenseRecord? renewFrom = null)
    {
        var dialog = new IssueLicenseDialog(_services, product, renewFrom);
        if (Owner is { } owner && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;

        dialog.ShowDialog();
        return dialog.Generated;
    }

    public bool Confirm(string title, string message, string confirmLabel, bool danger = false) =>
        ConfirmDialog.Show(Owner, title, message, confirmLabel, danger);

    public void ShowInfo(string title, string message) => MessageDialog.Show(Owner, title, message, isError: false);

    public void ShowError(string title, string message) => MessageDialog.Show(Owner, title, message, isError: true);

    public string? PromptForSigningPassword(string reason)
    {
        if (_services.Signing.IsUnlocked) return _services.Signing.Password;

        string? password = PasswordPromptDialog.Show(Owner, reason, _services.Signing.ValidatePassword);
        if (password is not null) _services.Signing.Unlock(password);

        return password;
    }

    public string? SaveTextFile(string title, string suggestedName, string filter, string content, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedName,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Directory.Exists(LicensePaths.Exports) ? LicensePaths.Exports : LicensePaths.Root
        };

        if (dialog.ShowDialog(Owner) != true) return null;

        try
        {
            string? directory = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            ShowError(L.T("S_CouldNotSaveFile"), ex.Message);
            return null;
        }
    }

    public string? OpenTextFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };

        if (dialog.ShowDialog(Owner) != true) return null;

        try
        {
            return File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(L.T("S_CouldNotReadFile"), ex.Message);
            return null;
        }
    }

    public void ShowLicenseDetails(string licenseText)
    {
        var dialog = new LicenseDetailsDialog(_services, licenseText);
        if (Owner is { } owner && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            ShowError(L.T("S_ClipboardUnavailable"), L.T("S_ClipboardUnavailableMessage"));
        }
    }
}
