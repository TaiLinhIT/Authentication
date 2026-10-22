namespace LicenseShared;

/// <summary>
/// Local data layout shared by the issuer tool and the client.
/// Tests can override <see cref="Root"/> to isolate state.
/// </summary>
public static class LicensePaths
{
    private static string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarehouseLicense");

    public static string Root
    {
        get => _root;
        set => _root = value;
    }

    public static string Keys => Path.Combine(Root, "keys");
    public static string Data => Path.Combine(Root, "data");
    public static string Licenses => Path.Combine(Root, "licenses");
    public static string State => Path.Combine(Root, "state");
    public static string Exports => Path.Combine(Root, "exports");

    public static void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Keys);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Licenses);
        Directory.CreateDirectory(State);
        Directory.CreateDirectory(Exports);
    }
}
