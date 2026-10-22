using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class BindingOption
{
    public BindingOption(DeviceBindingMode mode, string label, string hint)
    {
        Mode = mode;
        Label = label;
        Hint = hint;
    }

    public DeviceBindingMode Mode { get; }
    public string Label { get; }
    public string Hint { get; }
}

public sealed class LicenseTypeOption
{
    public LicenseTypeOption(LicenseType type, string label)
    {
        Type = type;
        Label = label;
    }

    public LicenseType Type { get; }
    public string Label { get; }
}

public sealed class IssueLicenseViewModel : ObservableObject
{
    private readonly AppServices _services;

    private ProductDefinition? _selectedProduct;
    private string _customerName = "";
    private string _stationName = "";
    private BindingOption? _selectedBinding;
    private string _deviceId = "";
    private LicenseTypeOption? _selectedType;
    private string _startDateText = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _expiryDateText = "";
    private string _errorMessage = "";
    private string _resultStatus = "";
    private bool _hasResult;
    private string _resultCode = "";
    private string _resultSerial = "";
    private string _resultAlgorithm = "";
    private string _resultExpiry = "";
    private string _resultStation = "";
    private string _resultFileName = "";
    private int _resultCodeLength;
    private bool _suppressAutoExpiry;

    public IssueLicenseViewModel(AppServices services, ProductDefinition? product, LicenseRecord? renewFrom)
    {
        _services = services;

        Products = new ObservableCollection<ProductDefinition>(
            services.Products.Load().OrderBy(p => p.ProductName, StringComparer.OrdinalIgnoreCase));

        IsProductLocked = product is not null || renewFrom is not null;

        BindingOptions = new ObservableCollection<BindingOption>();
        TypeOptions = new ObservableCollection<LicenseTypeOption>();

        BuildBindingOptions(DeviceBindingMode.MachineFingerprint);
        BuildTypeOptions(LicenseType.Days90);

        if (renewFrom is not null)
        {
            SelectedProduct = Products.FirstOrDefault(p =>
                string.Equals(p.ProductId, renewFrom.ProductId, StringComparison.OrdinalIgnoreCase));
            CustomerName = renewFrom.CustomerName;
            StationName = renewFrom.StationName;
            SelectedBinding = BindingOptions.First(b => b.Mode == renewFrom.BindingMode);
            DeviceId = renewFrom.DeviceId;
            SelectedType = TypeOptions.FirstOrDefault(t => t.Type == renewFrom.LicenseType) ?? SelectedType;
            StartDateText = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }
        else
        {
            SelectedProduct = product ?? Products.FirstOrDefault();
            if (product is not null) CustomerName = "";
        }

        GenerateCommand = new RelayCommand(Generate);
        CopyResultCommand = new RelayCommand(CopyResult);
        SaveResultCommand = new RelayCommand(SaveResult);
        SaveOfficeCommand = new RelayCommand(SaveOffice);
        ResetCommand = new RelayCommand(Reset);
        PasteDeviceCommand = new RelayCommand(PasteDevice);

        _suppressAutoExpiry = false;
        RecomputeExpiry();
        UpdateDeviceHint();
    }

    public ObservableCollection<ProductDefinition> Products { get; }
    public ObservableCollection<BindingOption> BindingOptions { get; }
    public ObservableCollection<LicenseTypeOption> TypeOptions { get; }

    public bool LockProduct { get; private init; }

    public bool IsProductLocked { get; private set; }

    public ProductDefinition? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (Set(ref _selectedProduct, value)) UpdatePreview();
        }
    }

    public string CustomerName
    {
        get => _customerName;
        set { if (Set(ref _customerName, value)) UpdatePreview(); }
    }

    public string StationName
    {
        get => _stationName;
        set { if (Set(ref _stationName, value)) UpdatePreview(); }
    }

    public BindingOption? SelectedBinding
    {
        get => _selectedBinding;
        set
        {
            if (Set(ref _selectedBinding, value))
            {
                UpdateDeviceHint();
                UpdatePreview();
            }
        }
    }

    public string DeviceId
    {
        get => _deviceId;
        set { if (Set(ref _deviceId, value)) UpdatePreview(); }
    }

    public string DeviceHint => SelectedBinding?.Hint ?? "";

    public LicenseTypeOption? SelectedType
    {
        get => _selectedType;
        set
        {
            if (Set(ref _selectedType, value))
            {
                OnPropertyChanged(nameof(IsPerpetual));
                RecomputeExpiry();
            }
        }
    }

    public string StartDateText
    {
        get => _startDateText;
        set
        {
            if (Set(ref _startDateText, value))
            {
                if (SelectedType?.Type is not LicenseType.Custom and not LicenseType.Perpetual)
                    RecomputeExpiry();
                else
                    UpdatePreview();
            }
        }
    }

    public string ExpiryDateText
    {
        get => _expiryDateText;
        set
        {
            if (Set(ref _expiryDateText, value)) UpdatePreview();
        }
    }

    public bool IsPerpetual => SelectedType?.Type == LicenseType.Perpetual;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    // ---------------------------------------------------------------- preview

    public string PreviewProduct => SelectedProduct?.ProductName ?? L.T("S_NotSelected");
    public string PreviewCustomer => string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName.Trim();
    public string PreviewDevice => string.IsNullOrWhiteSpace(DeviceId)
        ? L.T("S_NotSet")
        : DeviceId.Length > 38 ? DeviceId[..38] + "…" : DeviceId.Trim();
    public string PreviewBinding => SelectedBinding?.Label ?? L.T("S_BindingFingerprintLabel");
    public string PreviewDuration => IsPerpetual ? L.T("S_Perpetual") : SelectedType?.Label ?? "—";
    public string PreviewExpiry => IsPerpetual ? L.T("S_NoExpiry") : string.IsNullOrWhiteSpace(ExpiryDateText) ? "—" : ExpiryDateText.Trim();

    public string PreviewKeyHint => SelectedProduct is null
        ? ""
        : SelectedProduct.Algorithm == SigningAlgorithm.Ecdsa
            ? L.T("S_CompactCodeHint")
            : L.T("S_RsaCodeHint");

    // ---------------------------------------------------------------- result

    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }
    public string ResultCode { get => _resultCode; private set => Set(ref _resultCode, value); }
    public string ResultSerial { get => _resultSerial; private set => Set(ref _resultSerial, value); }
    public string ResultAlgorithm { get => _resultAlgorithm; private set => Set(ref _resultAlgorithm, value); }
    public string ResultExpiry { get => _resultExpiry; private set => Set(ref _resultExpiry, value); }
    public string ResultStation { get => _resultStation; private set => Set(ref _resultStation, value); }
    public string ResultFileName { get => _resultFileName; private set => Set(ref _resultFileName, value); }
    public int ResultCodeLength { get => _resultCodeLength; private set => Set(ref _resultCodeLength, value); }
    public string ResultStatus { get => _resultStatus; private set { if (Set(ref _resultStatus, value)) OnPropertyChanged(nameof(HasResultStatus)); } }
    public bool HasResultStatus => !string.IsNullOrWhiteSpace(ResultStatus);

    public string ResultDeliveryHint => ResultCodeLength > 0
        ? L.F("S_ResultCodeHint", ResultCodeLength)
        : "";

    public ICommand GenerateCommand { get; }
    public ICommand CopyResultCommand { get; }
    public ICommand SaveResultCommand { get; }
    public ICommand SaveOfficeCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand PasteDeviceCommand { get; }

    public LicenseRecord? GeneratedRecord { get; private set; }

    // ---------------------------------------------------------------- actions

    private void Generate()
    {
        ErrorMessage = "";
        SetResultStatus(null);

        try
        {
            if (SelectedProduct is null)
                throw new InvalidOperationException(L.T("S_ErrSelectProduct"));

            if (string.IsNullOrWhiteSpace(DeviceId))
                throw new InvalidOperationException(L.T("S_ErrDeviceRequired"));

            string device = DeviceId.Trim();
            if (SelectedBinding?.Mode == DeviceBindingMode.MachineFingerprint &&
                (device.Length != 64 || !device.All(Uri.IsHexDigit)))
            {
                throw new InvalidOperationException(L.T("S_ErrFingerprint"));
            }

            if (!TryParseDate(StartDateText, out var start))
                throw new InvalidOperationException(L.T("S_ErrStartDate"));

            bool perpetual = IsPerpetual;
            DateOnly expiry = start;
            TimeOnly? expiryTime = null;
            if (!perpetual)
            {
                if (!TryParseExpiry(ExpiryDateText, out expiry, out expiryTime))
                    throw new InvalidOperationException(L.T("S_ErrExpiryDate"));
                if (LicenseDurations.ExpiryInstantUtc(expiry, expiryTime) <
                    new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)))
                    throw new InvalidOperationException(L.T("S_ErrExpiryOrder"));
            }

            string? password = _services.Dialogs.PromptForSigningPassword(L.T("S_PasswordPromptSign"));
            if (password is null) return;

            var request = new IssueRequest
            {
                CustomerName = CustomerName,
                StationName = StationName,
                BindingMode = SelectedBinding?.Mode ?? DeviceBindingMode.MachineFingerprint,
                DeviceId = device,
                LicenseType = SelectedType?.Type ?? LicenseType.Days90,
                StartDate = start,
                ExpiryDate = expiry,
                ExpiryTime = expiryTime,
                Perpetual = perpetual
            };

            var record = _services.Issuer.Issue(SelectedProduct, request, password);
            ShowResult(record);
        }
        catch (CryptographicException ex)
        {
            _services.Signing.Lock();
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ShowResult(LicenseRecord record)
    {
        GeneratedRecord = record;

        string code = "";
        try
        {
            if (LicenseCodec.TryParseLicenseText(record.LicenseText, out var envelope, out string parseError))
                code = LicenseCodec.CreateActivationCode(envelope!);
            else
                SetResultStatus("S_LicenseInvalid");
        }
        catch (Exception)
        {
            SetResultStatus("S_LicenseInvalid");
        }

        ResultSerial = record.LicenseId;
        ResultCode = code;
        ResultCodeLength = code.Length;
        ResultAlgorithm = record.Algorithm;
        ResultExpiry = record.IsPerpetual ? L.T("S_Never") : LicenseFormatting.Expiry(record.ExpiresAtUtc);
        ResultStation = string.IsNullOrWhiteSpace(record.StationName) ? "—" : record.StationName;
        ResultFileName = $"{LicenseStore.SafeName(record.LicenseId)}.license.json";
        OnPropertyChanged(nameof(ResultDeliveryHint));
        HasResult = true;
    }

    private void CopyResult()
    {
        if (string.IsNullOrWhiteSpace(ResultCode)) return;

        _services.Dialogs.CopyToClipboard(ResultCode);
        SetResultStatus("S_CodeCopied");
    }

    private void SaveResult()
    {
        if (GeneratedRecord is null) return;

        string? path = _services.Dialogs.SaveTextFile(
            L.T("S_SaveSignedLicense"),
            ResultFileName,
            L.T("S_FilterLicenseFile"),
            GeneratedRecord.LicenseText,
            "license.json");

        if (path is not null) SetResultStatus("S_SavedTo", path);
    }

    private void SaveOffice()
    {
        if (GeneratedRecord is null) return;

        try
        {
            byte[] secret = _services.OfficeSecret.LoadOrCreate();
            byte[] blob = _services.Export.BuildOfficeFile(GeneratedRecord, secret);

            string? path = _services.Dialogs.SaveBinaryFile(
                L.T("S_SaveOfficeDat"),
                OfficeLicenseFile.DefaultFileName,
                L.T("S_FilterOfficeDat"),
                blob,
                "dat");

            if (path is not null) SetResultStatus("S_SavedTo", path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or ArgumentException)
        {
            SetResultStatus(null);
            ErrorMessage = ex.Message;
        }
    }

    private void Reset()
    {
        HasResult = false;
        GeneratedRecord = null;
        ResultCode = "";
        ResultSerial = "";
        ResultAlgorithm = "";
        ResultExpiry = "";
        ResultStation = "";
        ResultFileName = "";
        ResultCodeLength = 0;
        SetResultStatus(null);
        ErrorMessage = "";
        DeviceId = "";
        RecomputeExpiry();
        OnPropertyChanged(nameof(ResultDeliveryHint));
    }

    private void PasteDevice()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                DeviceId = System.Windows.Clipboard.GetText().Trim();
        }
        catch (Exception)
        {
            // Clipboard may be locked by another process.
        }
    }

    // ---------------------------------------------------------------- helpers

    private void BuildBindingOptions(DeviceBindingMode selected)
    {
        BindingOptions.Clear();
        BindingOptions.Add(new BindingOption(
            DeviceBindingMode.MachineFingerprint,
            L.T("S_BindingFingerprintLabel"),
            L.T("S_BindingFingerprintHint")));
        BindingOptions.Add(new BindingOption(
            DeviceBindingMode.ManualDeviceId,
            L.T("S_BindingDeviceId"),
            L.T("S_BindingDeviceIdHint")));

        _selectedBinding = BindingOptions.FirstOrDefault(b => b.Mode == selected) ?? BindingOptions[0];
        OnPropertyChanged(nameof(SelectedBinding));
        OnPropertyChanged(nameof(DeviceHint));
    }

    private void BuildTypeOptions(LicenseType selected)
    {
        TypeOptions.Clear();
        foreach (LicenseType type in Enum.GetValues<LicenseType>())
            TypeOptions.Add(new LicenseTypeOption(type, L.TypeName(type)));

        _selectedType = TypeOptions.FirstOrDefault(t => t.Type == selected) ?? TypeOptions[0];
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(IsPerpetual));
    }

    private void SetResultStatus(string? key, params object?[] args)
    {
        ResultStatus = key is null
            ? ""
            : args.Length == 0 ? L.T(key) : L.F(key, args);
    }

    private void UpdateDeviceHint() => OnPropertyChanged(nameof(DeviceHint));

    private void RecomputeExpiry()
    {
        if (_suppressAutoExpiry) return;

        var type = SelectedType?.Type ?? LicenseType.Days90;

        if (type == LicenseType.Perpetual)
        {
            _suppressAutoExpiry = true;
            ExpiryDateText = L.T("S_Never");
            _suppressAutoExpiry = false;
            UpdatePreview();
            return;
        }

        if (type != LicenseType.Custom && TryParseDate(StartDateText, out var start))
        {
            var expiry = LicenseDurations.DefaultExpiryDate(type, start);
            if (expiry is not null)
            {
                _suppressAutoExpiry = true;
                ExpiryDateText = expiry.Value.ToString("yyyy-MM-dd");
                _suppressAutoExpiry = false;
            }
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        OnPropertyChanged(nameof(PreviewProduct));
        OnPropertyChanged(nameof(PreviewCustomer));
        OnPropertyChanged(nameof(PreviewDevice));
        OnPropertyChanged(nameof(PreviewBinding));
        OnPropertyChanged(nameof(PreviewDuration));
        OnPropertyChanged(nameof(PreviewExpiry));
        OnPropertyChanged(nameof(PreviewKeyHint));
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.Equals("Never", StringComparison.OrdinalIgnoreCase)) return false;

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Parses an expiry that may carry a time of day: <c>yyyy-MM-dd</c>, <c>yyyy-MM-dd HH:mm</c>
    /// or <c>yyyy-MM-dd HH:mm:ss</c> (UTC). A date-only value leaves <paramref name="time"/> null
    /// so the license expires at the end of that day.
    /// </summary>
    private static bool TryParseExpiry(string? text, out DateOnly date, out TimeOnly? time)
    {
        date = default;
        time = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.Equals("Never", StringComparison.OrdinalIgnoreCase)) return false;

        string[] exactFormats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
        if (DateTimeOffset.TryParseExact(value, exactFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var stamp))
        {
            date = DateOnly.FromDateTime(stamp.DateTime);
            // A date-only entry (10 characters, "yyyy-MM-dd") keeps the end-of-day default.
            time = value.Length == 10 ? null : new TimeOnly(stamp.Hour, stamp.Minute, stamp.Second);
            return true;
        }

        // Lenient fallbacks (culture formats, extra whitespace) — accept a date with or without time.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp)
            || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out stamp))
        {
            date = DateOnly.FromDateTime(stamp.DateTime);
            var tod = stamp.TimeOfDay;
            time = tod == TimeSpan.Zero ? null : new TimeOnly(tod.Hours, tod.Minutes, tod.Seconds);
            return true;
        }

        return false;
    }
}
