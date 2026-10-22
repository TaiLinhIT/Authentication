namespace LicenseGenerator.Services;

public static class ProductIdFactory
{
    /// <summary>
    /// Builds a stable, file-system friendly product id from the display name.
    /// The id is what the client matches against its appsettings.json.
    /// </summary>
    public static string Create(string name)
    {
        var buffer = new System.Text.StringBuilder(name.Length);

        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                buffer.Append(c);
            else if (c is ' ' or '/' or '\\' or ':')
                buffer.Append('-');
        }

        string value = System.Text.RegularExpressions.Regex
            .Replace(buffer.ToString(), "-{2,}", "-")
            .Trim('-', '.', '_', ' ');

        if (value.Length == 0) return "Product";
        return value.Length <= 64 ? value : value[..64].TrimEnd('-', '.', '_');
    }
}
