using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseLocalization;
using LicenseShared;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    private NavItem? _selectedNav;
    private object? _currentPage;
    private string _pageTitle = "Dashboard";
    private string _pageSubtitle = "";
    private string _errorBanner = "";

    public MainViewModel(AppServices services)
    {
        _services = services;

        Dashboard = new DashboardViewModel(services, RefreshAll);
        Products = new ProductsViewModel(services, RefreshAll);
        Licenses = new LicensesViewModel(services, RefreshAll);
        Settings = new SettingsViewModel(services, RefreshKeyState);

        NavItems = new ObservableCollection<NavItem>
        {
            new(L.T("S_NavDashboard"), "\uE80F", Dashboard, L.T("S_NavDashboardSub")),
            new(L.T("S_NavProducts"), "\uE719", Products, L.T("S_NavProductsSub")),
            new(L.T("S_NavLicenses"), "\uE8A5", Licenses, L.T("S_NavLicensesSub")),
            new(L.T("S_NavSettings"), "\uE713", Settings, L.T("S_NavSettingsSub"))
        };

        AddProductCommand = new RelayCommand(AddProduct);
        IssueLicenseCommand = new RelayCommand(IssueLicense);
        UnlockCommand = new RelayCommand(Unlock);

        SelectedNav = NavItems[0];
        RefreshAll();

        LanguageManager.LanguageChanged += (_, _) => ApplyLanguage();
    }

    public DashboardViewModel Dashboard { get; }
    public ProductsViewModel Products { get; }
    public LicensesViewModel Licenses { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!Set(ref _selectedNav, value) || value is null) return;

            CurrentPage = value.Page;
            PageTitle = value.Title;
            PageSubtitle = value.Subtitle;
            RefreshPage();
        }
    }

    public object? CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }

    public string PageTitle { get => _pageTitle; private set => Set(ref _pageTitle, value); }

    public string PageSubtitle { get => _pageSubtitle; private set => Set(ref _pageSubtitle, value); }

    public string ErrorBanner
    {
        get => _errorBanner;
        private set
        {
            if (Set(ref _errorBanner, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorBanner);

    public bool IsUnlocked => _services.Signing.IsUnlocked;
    public string KeyStatusShort => IsUnlocked ? L.T("S_KeyUnlocked") : L.T("S_KeyLocked");
    public string KeyStatusHint => IsUnlocked ? L.T("S_ReadyToIssue") : L.T("S_UnlockToSign");
    public string KeyStateKind => IsUnlocked ? "success" : "warning";

    public ICommand AddProductCommand { get; }
    public ICommand IssueLicenseCommand { get; }
    public ICommand UnlockCommand { get; }

    public void RefreshAll()
    {
        try
        {
            Dashboard.Refresh();
            Products.Refresh();
            Licenses.Refresh();
            Settings.Refresh();

            int productCount = _services.Products.Load().Count;
            NavItems[1].Badge = productCount == 0 ? "" : productCount.ToString();
            NavItems[2].Badge = Licenses.SummaryText;
            ErrorBanner = "";
            RefreshKeyState();
        }
        catch (Exception ex)
        {
            ErrorBanner = ex.Message;
        }
    }

    private void RefreshPage()
    {
        try
        {
            switch (CurrentPage)
            {
                case DashboardViewModel dashboard: dashboard.Refresh(); break;
                case ProductsViewModel products: products.Refresh(); break;
                case LicensesViewModel licenses: licenses.Refresh(); break;
                case SettingsViewModel settings: settings.Refresh(); break;
            }

            ErrorBanner = "";
        }
        catch (Exception ex)
        {
            ErrorBanner = ex.Message;
        }
    }

    private void RefreshKeyState()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(KeyStatusShort));
        OnPropertyChanged(nameof(KeyStatusHint));
        OnPropertyChanged(nameof(KeyStateKind));
    }

    private void ApplyLanguage()
    {
        NavItems[0].Title = L.T("S_NavDashboard");
        NavItems[0].Subtitle = L.T("S_NavDashboardSub");
        NavItems[1].Title = L.T("S_NavProducts");
        NavItems[1].Subtitle = L.T("S_NavProductsSub");
        NavItems[2].Title = L.T("S_NavLicenses");
        NavItems[2].Subtitle = L.T("S_NavLicensesSub");
        NavItems[3].Title = L.T("S_NavSettings");
        NavItems[3].Subtitle = L.T("S_NavSettingsSub");

        if (SelectedNav is not null)
        {
            PageTitle = SelectedNav.Title;
            PageSubtitle = SelectedNav.Subtitle;
        }

        RefreshKeyState();
        RefreshAll();
    }

    private void AddProduct()
    {
        var product = _services.Dialogs.ShowAddProduct();
        if (product is null) return;

        try
        {
            _services.Products.Add(product);
            RefreshAll();

            var nav = NavItems.FirstOrDefault(n => n.Page == Products);
            if (nav is not null) SelectedNav = nav;
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotSaveProduct"), ex.Message);
        }
    }

    private void IssueLicense()
    {
        if (_services.Dialogs.ShowIssueLicense()) RefreshAll();
    }

    private void Unlock()
    {
        _services.Dialogs.PromptForSigningPassword(L.T("S_PasswordPromptUnlock"));
        RefreshKeyState();
        Settings.Refresh();
    }
}
