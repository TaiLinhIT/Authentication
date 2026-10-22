using LicenseGenerator.Infrastructure;

namespace LicenseGenerator.ViewModels;

public sealed class NavItem : ObservableObject
{
    private string _title;
    private string _subtitle;
    private string _badge = "";

    public NavItem(string title, string icon, object page, string subtitle)
    {
        _title = title;
        Icon = icon;
        Page = page;
        _subtitle = subtitle;
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Icon { get; }
    public object Page { get; }

    public string Subtitle
    {
        get => _subtitle;
        set => Set(ref _subtitle, value);
    }

    public string Badge
    {
        get => _badge;
        set => Set(ref _badge, value);
    }
}

public sealed class StatCard
{
    public StatCard(string label, string caption, string value, string icon, string kind)
    {
        Label = label;
        Caption = caption;
        Value = value;
        Icon = icon;
        Kind = kind;
    }

    public string Label { get; }
    public string Caption { get; }
    public string Value { get; }
    public string Icon { get; }
    public string Kind { get; }
}
