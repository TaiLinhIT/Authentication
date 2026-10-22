using LicenseShared;

namespace LicenseGenerator.Services;

public interface IDialogService
{
    /// <summary>Shows the add/edit product dialog. Returns the saved definition, or null when cancelled.</summary>
    ProductDefinition? ShowAddProduct(ProductDefinition? existing = null);

    /// <summary>Shows the issue-license workflow. Returns true when a license was generated.</summary>
    bool ShowIssueLicense(ProductDefinition? product = null, LicenseRecord? renewFrom = null);

    bool Confirm(string title, string message, string confirmLabel, bool danger = false);

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);

    /// <summary>Prompts for the signing-key password and validates it against existing keys.</summary>
    string? PromptForSigningPassword(string reason);

    /// <summary>Saves text to a file chosen by the user. Returns the path or null.</summary>
    string? SaveTextFile(string title, string suggestedName, string filter, string content, string defaultExtension);

    /// <summary>Opens a file and returns its content, or null.</summary>
    string? OpenTextFile(string title, string filter);

    void ShowLicenseDetails(string licenseText);

    void CopyToClipboard(string text);
}

public sealed class AppServices
{
    public AppServices()
    {
        LicensePaths.EnsureBaseDirectories();

        Products = new ProductStore();
        Records = new LicenseRecordStore();
        Keys = new KeyStore();
        Signing = new SigningSession(Keys);
        Issuer = new IssuerService(Keys, Records);
        Export = new ExportService(Keys);
        Dialogs = new DialogService(this);
    }

    public ProductStore Products { get; }
    public LicenseRecordStore Records { get; }
    public KeyStore Keys { get; }
    public SigningSession Signing { get; }
    public IssuerService Issuer { get; }
    public ExportService Export { get; }
    public IDialogService Dialogs { get; }
}
