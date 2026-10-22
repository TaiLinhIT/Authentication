namespace LicenseShared;

/// <summary>Display helpers for license dates shared by the issuer and the client.</summary>
public static class LicenseFormatting
{
    /// <summary>At or past this time of day the instant is treated as "end of the day".</summary>
    private static readonly TimeSpan EndOfDayThreshold = new(0, 23, 59, 59);

    /// <summary>
    /// Formats an expiry instant as <c>yyyy-MM-dd</c> when the license ends at midnight or at the
    /// end of the day (the date-only default), and as <c>yyyy-MM-dd HH:mm</c> when a specific
    /// hour/minute was chosen. Perpetual sentinels (<see cref="DateTimeOffset.MaxValue"/>) are
    /// date-only; callers are expected to render those as "never".
    /// </summary>
    /// <param name="expiresAtUtc">The signed expiry instant (UTC).</param>
    /// <param name="toLocal">Convert to the machine's local time before formatting.</param>
    public static string Expiry(DateTimeOffset expiresAtUtc, bool toLocal = true)
    {
        var value = toLocal ? expiresAtUtc.ToLocalTime() : expiresAtUtc;
        bool dateOnly = expiresAtUtc.TimeOfDay == TimeSpan.Zero
                        || expiresAtUtc.TimeOfDay >= EndOfDayThreshold;
        return value.ToString(dateOnly ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm");
    }
}
