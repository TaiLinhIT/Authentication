using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _refreshAll;

    private int _productCount;
    private int _licenseCount;
    private int _activeCount;
    private int _expiringCount;
    private int _keyCount;

    public DashboardViewModel(AppServices services, Action refreshAll)
    {
        _services = services;
        _refreshAll = refreshAll;

        AddProductCommand = new RelayCommand(AddProduct);
        IssueLicenseCommand = new RelayCommand(IssueLicense);
        ExportBundleCommand = new RelayCommand(ExportBundle);
    }

    public ObservableCollection<LicenseRowViewModel> Recent { get; } = new();

    public bool HasRecent => Recent.Count > 0;

    public int ProductCount { get => _productCount; private set => Set(ref _productCount, value); }
    public int LicenseCount { get => _licenseCount; private set => Set(ref _licenseCount, value); }
    public int ActiveCount { get => _activeCount; private set => Set(ref _activeCount, value); }
    public int ExpiringCount { get => _expiringCount; private set => Set(ref _expiringCount, value); }
    public int KeyCount { get => _keyCount; private set => Set(ref _keyCount, value); }

    public ICommand AddProductCommand { get; }
    public ICommand IssueLicenseCommand { get; }
    public ICommand ExportBundleCommand { get; }

    public ObservableCollection<StatCard> Stats { get; } = new();

    public void Refresh()
    {
        var products = _services.Products.Load();
        var records = _services.Records.Load();
        var now = DateTimeOffset.UtcNow;

        ProductCount = products.Count;
        LicenseCount = records.Count;
        ActiveCount = records.Count(r =>
        {
            var state = LicenseRecordStatus.Evaluate(r, now);
            return state is LicenseRecordState.Active or LicenseRecordState.Perpetual or LicenseRecordState.Expiring;
        });
        ExpiringCount = records.Count(r => LicenseRecordStatus.Evaluate(r, now) == LicenseRecordState.Expiring);
        KeyCount = products.Count(p => _services.Keys.HasKeyPair(p.ProductId));

        Stats.Clear();
        Stats.Add(new StatCard(L.T("S_StatProducts"), L.T("S_StatProductsCaption"), ProductCount.ToString(), "\uE7B8", "accent"));
        Stats.Add(new StatCard(L.T("S_StatLicenses"), L.T("S_StatLicensesCaption"), LicenseCount.ToString(), "\uE8A5", "accent"));
        Stats.Add(new StatCard(L.T("S_StatActive"), L.T("S_StatActiveCaption"), ActiveCount.ToString(), "\uE73E", "success"));
        Stats.Add(new StatCard(L.T("S_StatExpiring"), L.T("S_StatExpiringCaption"), ExpiringCount.ToString(), "\uE7BA", "warning"));

        Recent.Clear();
        foreach (var record in records.OrderByDescending(r => r.IssuedAtUtc).Take(5))
            Recent.Add(new LicenseRowViewModel(record, _services, _refreshAll));

        OnPropertyChanged(nameof(HasRecent));
    }

    private void AddProduct()
    {
        var product = _services.Dialogs.ShowAddProduct();
        if (product is null) return;

        try
        {
            _services.Products.Add(product);
            _refreshAll();
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotSaveProduct"), ex.Message);
        }
    }

    private void IssueLicense()
    {
        if (_services.Dialogs.ShowIssueLicense()) _refreshAll();
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
        _services.Dialogs.SaveTextFile(
            L.T("S_ExportClientKeysTitle"),
            "public-keys.json",
            L.T("S_FilterJson"),
            bundle,
            "json");
    }
}
