namespace LicenseShared;

/// <summary>Display helpers for license dates shared by the issuer and the client.</summary>
public static class LicenseFormatting
{
    /// <summary>At or past this time of day the instant is treated as "end of the day".</summary>
    private static readonly TimeSpan EndOfDayThreshold = new(0, 23, 59, 59);

    /// <summary>
    /// Formats an expiry instant as <c>yyyy-MM-dd</c> at the end of the displayed day,
    /// otherwise including the time (and seconds when nonzero). Explicit midnight remains
    /// visible. Perpetual sentinels (<see cref="DateTimeOffset.MaxValue"/>) are
    /// date-only; callers are expected to render those as "never".
    /// </summary>
    /// <param name="expiresAtUtc">The signed expiry instant (UTC).</param>
    /// <param name="toLocal">Convert to the machine's local time before formatting.</param>
    public static string Expiry(DateTimeOffset expiresAtUtc, bool toLocal = true)
    {
        var value = toLocal ? expiresAtUtc.ToLocalTime() : expiresAtUtc.ToUniversalTime();
        bool dateOnly = value.TimeOfDay >= EndOfDayThreshold;
        return value.ToString(dateOnly ? "yyyy-MM-dd" : value.Second == 0 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
