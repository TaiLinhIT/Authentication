using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace LicenseShared;

/// <summary>48-bit unicast adapter addresses, stored as twelve uppercase hex digits.</summary>
public static class MacAddress
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        string text = value?.Trim() ?? "";
        if (!Regex.IsMatch(text, @"\A(?:[0-9a-fA-F]{12}|(?:[0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2})\z"))
            return false;
        string hex = text.Replace("-", "").Replace(":", "").ToUpperInvariant();
        if (hex == "000000000000" || (Convert.ToByte(hex[..2], 16) & 1) != 0)
            return false;
        normalized = hex;
        return true;
    }

    /// <summary>Includes disconnected adapters; activation never requires network access.</summary>
    public static IReadOnlyList<string> GetCurrent()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .Select(n => n.GetPhysicalAddress().ToString())
                .Select(n => TryNormalize(n, out var value) ? value : "")
                .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (NetworkInformationException) { return Array.Empty<string>(); }
    }

    public static bool Matches(string? licensedAddress, IEnumerable<string> addresses) =>
        TryNormalize(licensedAddress, out var expected) && addresses.Any(address =>
            TryNormalize(address, out var actual) && expected == actual);
}
