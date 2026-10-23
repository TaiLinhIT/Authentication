using System.Globalization;

namespace LicenseShared;

/// <summary>Converts explicitly zoned wall-clock input to signed UTC instants.</summary>
public static class LicenseDateInput
{
    public static bool TryParseExpiry(string? text, out DateOnly date, out TimeOnly? time)
    {
        date = default;
        time = null;
        string value = text?.Trim() ?? "";
        if (!DateTime.TryParseExact(value,
                new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)) return false;
        date = DateOnly.FromDateTime(stamp);
        time = value.Length == 10 ? null : TimeOnly.FromDateTime(stamp);
        return true;
    }

    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var wallTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallTime) || zone.IsAmbiguousTime(wallTime))
            throw new ArgumentException("This local time is invalid or ambiguous because of daylight saving time.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallTime, zone));
    }

    public static DateTimeOffset ExpiryUtc(DateOnly date, TimeOnly? time, TimeZoneInfo zone) =>
        ToUtc(date, time ?? TimeOnly.MaxValue, zone);
}
