using LicenseGenerator.Infrastructure;
using LicenseGenerator.Localization;
using LicenseGenerator.Services;
using LicenseShared;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Input;

namespace LicenseGenerator.ViewModels;

public sealed class LicenseFilter
{
    public LicenseFilter(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Stable key used by the filter logic; never localized.</summary>
    public string Key { get; }

    public string Label { get; }
}

public sealed class LicenseRowViewModel
{
    private readonly AppServices _services;
    private readonly Action _refresh;

    public LicenseRowViewModel(LicenseRecord record, AppServices services, Action refresh)
    {
        Record = record;
        _services = services;
        _refresh = refresh;

        var state = LicenseRecordStatus.Evaluate(record, DateTimeOffset.UtcNow);
        State = state;
        StatusKind = state switch
        {
            LicenseRecordState.Active or LicenseRecordState.Perpetual => "success",
            LicenseRecordState.Expiring => "warning",
            LicenseRecordState.Scheduled => "accent",
            _ => "danger"
        };

        SaveOfficeFileCommand = new RelayCommand(SaveOfficeFile);
        InspectCommand = new RelayCommand(Inspect);
        RenewCommand = new RelayCommand(Renew);
        RevokeCommand = new RelayCommand(Revoke);
        DeleteCommand = new RelayCommand(Delete);
    }

    public LicenseRecord Record { get; }

    public string LicenseId => Record.LicenseId;
    public string ProductName => Record.ProductName;
    public string ProductId => Record.ProductId;
    public string CustomerName => string.IsNullOrWhiteSpace(Record.CustomerName) ? "—" : Record.CustomerName;
    public string StationName => string.IsNullOrWhiteSpace(Record.StationName) ? "—" : Record.StationName;
    public string DeviceId => Record.DeviceId;
    public string DeviceShort => Record.DeviceId.Length <= 18 ? Record.DeviceId : Record.DeviceId[..16] + "…";
    public string TypeLabel => L.TypeName(Record.LicenseType);
    public string BindingLabel => L.Binding(Record.BindingMode);
    public string AlgorithmLabel => Record.Algorithm;
    public string IssuedLabel => Record.IssuedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string ExpiresLabel => Record.IsPerpetual ? L.T("S_Never") : LicenseFormatting.Expiry(Record.ExpiresAtUtc);
    public LicenseRecordState State { get; }
    public string StateLabel => L.RecordState(State);
    public string StatusKind { get; }
    public string RevokeLabel => Record.Revoked ? L.T("S_Unrevoke") : L.T("S_Revoke");

    public ICommand SaveOfficeFileCommand { get; }
    public ICommand InspectCommand { get; }
    public ICommand RenewCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand DeleteCommand { get; }

    private void Inspect() => _services.Dialogs.ShowLicenseDetails(Record.LicenseText);

    private void SaveOfficeFile()
    {
        try
        {
            byte[] secret = _services.OfficeSecret.LoadOrCreate();
            byte[] blob = _services.Export.BuildOfficeFile(Record, secret);

            _services.Dialogs.SaveBinaryFile(
                L.T("S_SaveOfficeDat"),
                OfficeLicenseFile.DefaultFileName,
                L.T("S_FilterOfficeDat"),
                blob,
                "dat");
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or ArgumentException)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotSaveFile"), ex.Message);
        }
    }

    private void Renew()
    {
        var product = _services.Products.Load()
            .FirstOrDefault(p => string.Equals(p.ProductId, Record.ProductId, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            _services.Dialogs.ShowError(
                L.T("S_ProductNotFoundTitle"),
                L.F("S_ProductNotFoundMessage", Record.ProductName));
            return;
        }

        if (_services.Dialogs.ShowIssueLicense(product, Record)) _refresh();
    }

    private void Revoke()
    {
        bool revoking = !Record.Revoked;

        bool confirmed = _services.Dialogs.Confirm(
            revoking ? L.T("S_RevokeLicenseTitle") : L.T("S_UnrevokeLicenseTitle"),
            revoking
                ? L.F("S_RevokeMessage", Record.LicenseId)
                : L.F("S_UnrevokeMessage", Record.LicenseId),
            revoking ? L.T("S_Revoke") : L.T("S_Unrevoke"),
            danger: revoking);

        if (!confirmed) return;

        try
        {
            var records = _services.Records.Load();
            var stored = records.FirstOrDefault(r => r.LicenseId == Record.LicenseId);
            if (stored is null) return;

            stored.Revoked = revoking;
            stored.RevokedAtUtc = revoking ? DateTimeOffset.UtcNow : null;
            _services.Records.Save(records);
            _refresh();
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotUpdateRecord"), ex.Message);
        }
    }

    private void Delete()
    {
        bool confirmed = _services.Dialogs.Confirm(
            L.T("S_RemoveRecordTitle"),
            L.F("S_RemoveRecordMessage", Record.LicenseId),
            L.T("S_RemoveRecordLabel"),
            danger: true);

        if (!confirmed) return;

        try
        {
            _services.Records.Remove(Record.LicenseId);
            _refresh();
        }
        catch (Exception ex)
        {
            _services.Dialogs.ShowError(L.T("S_CouldNotRemoveRecord"), ex.Message);
        }
    }
}

public sealed class LicensesViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _refreshAll;
    private readonly List<LicenseRowViewModel> _all = new();

    private string _searchText = "";
    private LicenseFilter? _statusFilter;
    private string _countText = L.F("S_LicenseCountMany", 0);
    private string _summaryText = "";
    private bool _isEmpty = true;

    public LicensesViewModel(AppServices services, Action refreshAll)
    {
        _services = services;
        _refreshAll = refreshAll;

        RebuildFilters("All");

        IssueCommand = new RelayCommand(Issue);
        ExportCsvCommand = new RelayCommand(ExportCsv);
    }

    public ObservableCollection<LicenseRowViewModel> Items { get; } = new();

    public ObservableCollection<LicenseFilter> StatusFilters { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ApplyFilter(); }
    }

    public LicenseFilter? StatusFilter
    {
        get => _statusFilter;
        set { if (Set(ref _statusFilter, value)) ApplyFilter(); }
    }

    public string CountText { get => _countText; private set => Set(ref _countText, value); }
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public ICommand IssueCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public void Refresh()
    {
        RebuildFilters(_statusFilter?.Key ?? "All");
        _all.Clear();

        foreach (var record in _services.Records.Load().OrderByDescending(r => r.IssuedAtUtc))
            _all.Add(new LicenseRowViewModel(record, _services, _refreshAll));

        var now = DateTimeOffset.UtcNow;
        int active = _all.Count(r =>
        {
            var state = LicenseRecordStatus.Evaluate(r.Record, now);
            return state is LicenseRecordState.Active or LicenseRecordState.Perpetual;
        });
        int expiring = _all.Count(r => LicenseRecordStatus.Evaluate(r.Record, now) == LicenseRecordState.Expiring);
        int expired = _all.Count(r => LicenseRecordStatus.Evaluate(r.Record, now) == LicenseRecordState.Expired);

        SummaryText = L.F("S_Summary", active, expiring, expired);
        ApplyFilter();
    }

    private void RebuildFilters(string selectedKey)
    {
        StatusFilters.Clear();
        StatusFilters.Add(new LicenseFilter("All", L.T("S_FilterAll")));
        StatusFilters.Add(new LicenseFilter("Active", L.T("S_FilterActive")));
        StatusFilters.Add(new LicenseFilter("Expiring", L.T("S_FilterExpiring")));
        StatusFilters.Add(new LicenseFilter("Expired", L.T("S_FilterExpired")));
        StatusFilters.Add(new LicenseFilter("Scheduled", L.T("S_FilterScheduled")));
        StatusFilters.Add(new LicenseFilter("Revoked", L.T("S_FilterRevoked")));

        _statusFilter = StatusFilters.FirstOrDefault(f => f.Key == selectedKey) ?? StatusFilters[0];
        OnPropertyChanged(nameof(StatusFilter));
    }

    private void ApplyFilter()
    {
        string term = SearchText.Trim();

        IEnumerable<LicenseRowViewModel> filtered = _all;

        if (term.Length > 0)
        {
            filtered = filtered.Where(r =>
                r.LicenseId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.StationName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.DeviceId.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        filtered = _statusFilter?.Key switch
        {
            "Active" => filtered.Where(r =>
                r.State is LicenseRecordState.Active or LicenseRecordState.Perpetual),
            "Expiring" => filtered.Where(r => r.State == LicenseRecordState.Expiring),
            "Expired" => filtered.Where(r => r.State == LicenseRecordState.Expired),
            "Scheduled" => filtered.Where(r => r.State == LicenseRecordState.Scheduled),
            "Revoked" => filtered.Where(r => r.State == LicenseRecordState.Revoked),
            _ => filtered
        };

        Items.Clear();
        foreach (var row in filtered) Items.Add(row);

        CountText = Items.Count == 1 ? L.T("S_LicenseCountOne") : L.F("S_LicenseCountMany", Items.Count);
        IsEmpty = Items.Count == 0;
    }

    private void Issue()
    {
        if (_services.Dialogs.ShowIssueLicense()) _refreshAll();
    }

    private void ExportCsv()
    {
        var records = _services.Records.Load();
        if (records.Count == 0)
        {
            _services.Dialogs.ShowInfo(L.T("S_NothingToExportTitle"), L.T("S_NothingToExportMessage"));
            return;
        }

        _services.Dialogs.SaveTextFile(
            L.T("S_ExportRegisterTitle"),
            "issued-licenses.csv",
            L.T("S_FilterCsv"),
            ExportService.BuildCsv(records),
            "csv");
    }
}
