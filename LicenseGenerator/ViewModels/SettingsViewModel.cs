using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _stateChanged;

    public SettingsViewModel(AppServices services, Action stateChanged)
    {
        _services = services;
        _stateChanged = stateChanged;

        UnlockCommand = new RelayCommand(Unlock);
        LockCommand = new RelayCommand(Lock);
        ExportBundleCommand = new RelayCommand(ExportBundle);
        ExportOfficeSecretCommand = new RelayCommand(ExportOfficeSecret);
        ResetIssuerKeysCommand = new RelayCommand(ResetIssuerKeys);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(LicensePaths.Data));
        OpenKeysFolderCommand = new RelayCommand(() => OpenFolder(LicensePaths.Keys));
        CopyRootCommand = new RelayCommand(() => _services.Dialogs.CopyToClipboard(LicensePaths.Root));
    }

    public bool IsUnlocked => _services.Signing.IsUnlocked;
    public string StatusText => IsUnlocked ? L.T("S_UnlockedSession") : L.T("S_Locked");
    public string StatusKind => IsUnlocked ? "success" : "warning";
    public string StatusCaption => IsUnlocked
        ? L.T("S_UnlockedCaption")
        : L.T("S_LockedCaption");

    public string KeyCountText
    {
        get
        {
            var products = _services.Products.Load();
            int count = products.Count(p => _services.Keys.HasKeyPair(p.ProductId));
            return count == 1 ? L.T("S_ProductKeyOne") : L.F("S_ProductKeyMany", count);
        }
    }

    public string DataRoot => LicensePaths.Data;
    public string KeysRoot => LicensePaths.Keys;
    public string LicensesRoot => LicensePaths.Licenses;
    public string ProductsFile => _services.Products.FilePath;
    public string RecordsFile => _services.Records.FilePath;

    /// <summary>
    /// The reset is offered only after every product has been removed, so a leftover key that is
    /// still encrypted with the old issuer password can never sit next to newly created keys.
    /// </summary>
    public bool CanResetIssuerKeys => _services.Products.Load().Count == 0;

    public bool HasStoredKeys => _services.Keys.StoredKeyCount() > 0;

    public string StoredKeysInfo
    {
        get
        {
            int count = _services.Keys.StoredKeyCount();
            return count == 0
                ? L.T("S_IssuerKeysNone")
                : count == 1
                    ? L.T("S_IssuerKeysStoredOne")
                    : L.F("S_IssuerKeysStoredMany", count);
        }
    }

    public string BundleInfo
    {
        get
        {
            var products = _services.Products.Load();
            int ready = products.Count(p => _services.Keys.ReadPublicKey(p.ProductId) is not null);
            return ready == 1
                ? L.T("S_PublicKeyIncludedOne")
                : L.F("S_PublicKeyIncludedMany", ready);
        }
    }

    public ICommand UnlockCommand { get; }
    public ICommand LockCommand { get; }
    public ICommand ExportBundleCommand { get; }
    public ICommand ExportOfficeSecretCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenKeysFolderCommand { get; }
    public ICommand CopyRootCommand { get; }
    public ICommand ResetIssuerKeysCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(KeyCountText));
        OnPropertyChanged(nameof(BundleInfo));
        OnPropertyChanged(nameof(CanResetIssuerKeys));
        OnPropertyChanged(nameof(HasStoredKeys));
        OnPropertyChanged(nameof(StoredKeysInfo));
        OnPropertyChanged(nameof(DataRoot));
        OnPropertyChanged(nameof(KeysRoot));
        OnPropertyChanged(nameof(LicensesRoot));
        OnPropertyChanged(nameof(ProductsFile));
        OnPropertyChanged(nameof(RecordsFile));
    }

    private void Unlock()
    {
        if (_services.Dialogs.PromptForSigningPassword(L.T("S_PasswordPromptUnlock")) is null)
            return;

        Refresh();
        _stateChanged();
    }

    private void Lock()
    {
        _services.Signing.Lock();
        Refresh();
        _stateChanged();
    }

    /// <summary>
    /// Erases every stored key. Refuses to run while products exist: their keys must stay usable to
    /// renew the licenses that were already issued with them.
    /// </summary>
    private void ResetIssuerKeys()
    {
        if (!CanResetIssuerKeys || !HasStoredKeys) return;

        int count = _services.Keys.StoredKeyCount();
        bool confirmed = _services.Dialogs.Confirm(
            L.T("S_ResetKeysTitle"),
            count == 1 ? L.T("S_ResetKeysMessageOne") : L.F("S_ResetKeysMessageMany", count),
            L.T("S_ResetKeysConfirm"),
            danger: true);

        if (!confirmed) return;

        try
        {
            int removed = _services.Keys.DeleteAllKeys();
            _services.Signing.Lock();
            Refresh();
            _stateChanged();
            _services.Dialogs.ShowInfo(
                L.T("S_ResetKeysDoneTitle"),
                L.T("S_ResetKeysDoneMessage"));
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_ResetKeysFailedTitle"), ex.Message);
        }
    }

    private void ExportBundle()
    {
        var products = _services.Products.Load();
        if (products.Count == 0)
        {
            _services.Dialogs.ShowInfo(L.T("S_NoProductsTitle"), L.T("S_NoProductsBeforeExport"));
            return;
        }

        string bundle = _services.Export.BuildPublicKeyBundle(products);
        string? path = _services.Dialogs.SaveTextFile(
            L.T("S_ExportClientKeysTitle"),
            "public-keys.json",
            L.T("S_FilterJson"),
            bundle,
            "json");

        if (path is not null)
            _services.Dialogs.ShowInfo(
                L.T("S_PublicKeysExportedTitle"),
                L.F("S_PublicKeysExportedMessage", path));
    }

    private void ExportOfficeSecret()
    {
        string base64 = _services.OfficeSecret.ExportBase64();

        string? path = _services.Dialogs.SaveTextFile(
            L.T("S_ExportOfficeSecretTitle"),
            "office-secret.txt",
            L.T("S_FilterText"),
            base64,
            "txt");

        if (path is not null)
            _services.Dialogs.ShowInfo(
                L.T("S_OfficeSecretExportedTitle"),
                L.F("S_OfficeSecretExportedMessage", path));
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotOpenFolder"), ex.Message);
        }
    }
}
