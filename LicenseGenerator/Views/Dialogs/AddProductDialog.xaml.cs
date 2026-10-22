using LicenseGenerator.Interop;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Windows;

namespace LicenseGenerator.Views.Dialogs;

public partial class AddProductDialog : Window
{
    private readonly AppServices _services;
    private readonly ProductDefinition? _existing;
    private bool _initializing = true;

    public AddProductDialog(AppServices services, ProductDefinition? existing)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        _services = services;
        _existing = existing;

        NameBox.Text = existing?.ProductName ?? "";

        AlgorithmBox.ItemsSource = new[] { "RSA", "ECDSA" };
        KeygenBox.ItemsSource = new[] { L.T("S_KeygenSerial"), L.T("S_KeygenCodes") };

        AlgorithmBox.SelectedIndex = existing?.Algorithm == SigningAlgorithm.Ecdsa ? 1 : 0;
        KeygenBox.SelectedIndex = existing?.KeygenMode == KeygenMode.ActivationCodes ? 1 : 0;
        RefreshBits(existing?.KeySize ?? 3072);

        if (existing is not null)
        {
            Title = L.T("S_EditProductTitle");
            HeaderText.Text = L.T("S_EditProductTitle");
            SubHeaderText.Text = L.T("S_EditProductIntro");
            SaveButton.Content = L.T("S_SaveChanges");
            AlgorithmBox.IsEnabled = false;
            BitsBox.IsEnabled = false;
            EditNote.Visibility = Visibility.Visible;
        }

        UpdateKeygenHint();
        _initializing = false;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public ProductDefinition? Result { get; private set; }

    private void AlgorithmBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        int preferred = AlgorithmBox.SelectedIndex == 1 ? 256 : 3072;
        RefreshBits(preferred);
    }

    private void BitsBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Nothing to do; the value is read when the product is created.
    }

    private void KeygenBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdateKeygenHint();
    }

    private void RefreshBits(int preferred)
    {
        bool ecdsa = AlgorithmBox.SelectedIndex == 1;

        if (ecdsa)
        {
            BitsBox.ItemsSource = new[] { "P-256", "P-384" };
            BitsBox.SelectedItem = preferred >= 384 ? "P-384" : "P-256";
        }
        else
        {
            BitsBox.ItemsSource = new[] { "2048", "3072", "4096" };
            BitsBox.SelectedItem = preferred is 2048 or 4096 ? preferred.ToString() : "3072";
        }
    }

    private void UpdateKeygenHint()
    {
        bool activationCodes = KeygenBox.SelectedIndex == 1;

        KeygenHint.Text = activationCodes
            ? L.T("S_KeygenHintCodes")
            : L.T("S_KeygenHintSerials");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();

        if (name.Length < 2)
        {
            Error(L.T("S_ErrProductNameShort"));
            return;
        }

        if (name.Length > 80)
        {
            Error(L.T("S_ErrProductNameLong"));
            return;
        }

        var products = _services.Products.Load();
        bool duplicateName = products.Any(p =>
            !string.Equals(p.ProductId, _existing?.ProductId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ProductName, name, StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
        {
            Error(L.T("S_ErrProductDuplicate"));
            return;
        }

        if (_existing is not null)
        {
            _existing.ProductName = name;
            _existing.KeygenMode = KeygenBox.SelectedIndex == 1 ? KeygenMode.ActivationCodes : KeygenMode.SerialNumbers;
            Result = _existing;
            DialogResult = true;
            return;
        }

        var algorithm = AlgorithmBox.SelectedIndex == 1 ? SigningAlgorithm.Ecdsa : SigningAlgorithm.Rsa;
        int keySize = algorithm == SigningAlgorithm.Rsa
            ? int.Parse((string)BitsBox.SelectedItem)
            : ((string)BitsBox.SelectedItem) == "P-384" ? 384 : 256;

        string productId = ProductIdFactory.Create(name);

        if (products.Any(p => string.Equals(p.ProductId, productId, StringComparison.OrdinalIgnoreCase)) ||
            _services.Keys.HasKeyPair(productId))
        {
            Error(L.T("S_ErrProductIdDuplicate"));
            return;
        }

        string? password = _services.Dialogs.PromptForSigningPassword(L.T("S_PasswordPromptNewProduct"));
        if (password is null) return;

        var product = new ProductDefinition
        {
            ProductId = productId,
            ProductName = name,
            Algorithm = algorithm,
            KeySize = keySize,
            KeygenMode = KeygenBox.SelectedIndex == 1 ? KeygenMode.ActivationCodes : KeygenMode.SerialNumbers
        };

        try
        {
            _services.Keys.CreateKeyPair(product, password);
            _services.Signing.Unlock(password);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }

        Result = product;
        DialogResult = true;
    }

    private void Error(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
