using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class ProductRowViewModel
{
    private readonly AppServices _services;
    private readonly Action _refresh;

    public ProductRowViewModel(ProductDefinition product, int licenseCount, AppServices services, Action refresh)
    {
        Product = product;
        LicenseCount = licenseCount;
        _services = services;
        _refresh = refresh;

        IssueCommand = new RelayCommand(Issue);
        EditCommand = new RelayCommand(Edit);
        DeleteCommand = new RelayCommand(Delete);
        ExportPublicKeyCommand = new RelayCommand(ExportPublicKey);
        CopyIdCommand = new RelayCommand(CopyId);
    }

    public ProductDefinition Product { get; }
    public int LicenseCount { get; }

    public string Name => Product.ProductName;
    public string ProductId => Product.ProductId;
    public string Initials => Product.Initials;
    public string AlgorithmLabel => L.AlgorithmName(Product.Algorithm);
    public string KeygenModeLabel => L.KeygenName(Product.KeygenMode);
    public string KeygenKind => Product.KeygenMode == KeygenMode.ActivationCodes ? "accent" : "";
    public string KeyIdShort => string.IsNullOrEmpty(Product.KeyId) ? L.T("S_NoKey") : L.F("S_KeyPrefix", Product.KeyId);
    public string CreatedLabel => Product.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd");
    public string LicenseCountText => LicenseCount == 1 ? L.T("S_LicenseCountOne") : L.F("S_LicenseCountMany", LicenseCount);

    public ICommand IssueCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExportPublicKeyCommand { get; }
    public ICommand CopyIdCommand { get; }

    private void Issue()
    {
        if (_services.Dialogs.ShowIssueLicense(Product)) _refresh();
    }

    private void Edit()
    {
        var edited = _services.Dialogs.ShowAddProduct(Product);
        if (edited is null) return;

        try
        {
            _services.Products.Update(edited);
            _refresh();
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotUpdateProduct"), ex.Message);
        }
    }

    private void Delete()
    {
        bool confirmed = _services.Dialogs.Confirm(
            L.T("S_DeleteProductTitle"),
            L.F("S_DeleteProductMessage", Product.ProductName),
            L.T("S_DeleteProduct"),
            danger: true);

        if (!confirmed) return;

        try
        {
            _services.Products.Remove(Product.ProductId);
            _refresh();
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotDeleteProduct"), ex.Message);
        }
    }

    private void ExportPublicKey()
    {
        string? pem = _services.Keys.ReadPublicKey(Product.ProductId);
        if (pem is null)
        {
            _services.Dialogs.ShowError(L.T("S_PublicKeyMissingTitle"), L.T("S_PublicKeyMissingMessage"));
            return;
        }

        _services.Dialogs.SaveTextFile(
            L.T("S_ExportPublicKey"),
            $"{LicenseStore.SafeName(Product.ProductId)}-public.pem",
            L.T("S_FilterPem"),
            pem,
            "pem");
    }

    private void CopyId() => _services.Dialogs.CopyToClipboard(Product.ProductId);
}

public sealed class ProductsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _refreshAll;
    private readonly List<ProductRowViewModel> _all = new();

    private string _searchText = "";
    private string _countText = L.F("S_ProductCountMany", 0);
    private bool _isEmpty = true;

    public ProductsViewModel(AppServices services, Action refreshAll)
    {
        _services = services;
        _refreshAll = refreshAll;
        AddCommand = new RelayCommand(Add);
    }

    public ObservableCollection<ProductRowViewModel> Items { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value)) ApplyFilter();
        }
    }

    public string CountText { get => _countText; private set => Set(ref _countText, value); }
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public ICommand AddCommand { get; }

    public void Refresh()
    {
        var records = _services.Records.Load();
        _all.Clear();

        foreach (var product in _services.Products.Load().OrderBy(p => p.ProductName, StringComparer.OrdinalIgnoreCase))
        {
            int count = records.Count(r =>
                string.Equals(r.ProductId, product.ProductId, StringComparison.OrdinalIgnoreCase));
            _all.Add(new ProductRowViewModel(product, count, _services, _refreshAll));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string term = SearchText.Trim();
        var filtered = _all.Where(p =>
            term.Length == 0 ||
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.ProductId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.AlgorithmLabel.Contains(term, StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var row in filtered) Items.Add(row);

        CountText = Items.Count == 1 ? L.T("S_ProductCountOne") : L.F("S_ProductCountMany", Items.Count);
        IsEmpty = Items.Count == 0;
    }

    private void Add()
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
}
