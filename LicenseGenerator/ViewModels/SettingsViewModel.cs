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
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenKeysFolderCommand { get; }
    public ICommand CopyRootCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(KeyCountText));
        OnPropertyChanged(nameof(BundleInfo));
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
