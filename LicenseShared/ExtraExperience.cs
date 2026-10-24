namespace LicenseShared;

/// <summary>Signed, half-open UTC interval: StartUtc included, EndUtc excluded.</summary>
public sealed record ExperienceWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public static class ExtraExperience
{
    public const int MaxWindows = 800;

    public static bool Allows(IEnumerable<ExperienceWindow> windows, DateTimeOffset now) =>
        windows.Any(w => now >= w.StartUtc && now < w.EndUtc);

    public static void Validate(IReadOnlyList<ExperienceWindow> windows, DateTimeOffset expiry, bool perpetual)
    {
        if (windows.Count > MaxWindows || (perpetual && windows.Count != 0))
            throw new FormatException("Invalid extra experience schedule.");
        DateTimeOffset previous = expiry;
        foreach (var w in windows)
        {
            if (w.StartUtc < previous || w.EndUtc <= w.StartUtc ||
                w.EndUtc.UtcTicks - expiry.UtcTicks > TimeSpan.FromDays(33).Ticks)
                throw new FormatException("Invalid extra experience interval.");
            previous = w.EndUtc;
        }
    }

    public static List<ExperienceWindow> Build(DateOnly expiryDate, TimeOnly? expiryTime,
        TimeZoneInfo zone, IEnumerable<int> hours, IEnumerable<DateOnly> days)
    {
        var selectedHours = hours.Distinct().Order().ToArray();
        var selectedDays = days.Distinct().Order().ToArray();
        if (selectedHours.Any(h => h < 0 || h > 23)) throw new ArgumentException("Invalid hour slot.");
        if (selectedHours.Length == 0 && selectedDays.Length == 0) return new();
        var lastDay = expiryDate.AddMonths(1);
        if (selectedDays.Any(d => d <= expiryDate || d > lastDay))
            throw new ArgumentException("Extra days must be within one month after expiry.");
        var expiry = LicenseDateInput.ExpiryUtc(expiryDate, expiryTime, zone);
        var result = new List<ExperienceWindow>();
        if (selectedDays.Length == 0)
        {
            foreach (int h in selectedHours)
                result.Add(new(expiry.AddHours(h), expiry.AddHours(h + 1)));
        }
        else
        {
            var anchor = expiryTime ?? TimeOnly.MaxValue;
            DateTimeOffset Utc(DateTime t) => LicenseDateInput.ToUtc(DateOnly.FromDateTime(t), TimeOnly.FromDateTime(t), zone);
            foreach (var day in selectedDays)
            {
                var midnight = day.ToDateTime(TimeOnly.MinValue);
                var end = midnight.AddDays(1);
                if (selectedHours.Length == 0) result.Add(new(Utc(midnight), Utc(end)));
                else foreach (int h in selectedHours)
                {
                    // Repeat the selected clock interval on this date, clipping midnight crossings.
                    var time = anchor.AddHours(h);
                    foreach (int shift in new[] { -1, 0 })
                    {
                        var start = day.ToDateTime(time).AddDays(shift);
                        var stop = start.AddHours(1);
                        if (start < midnight) start = midnight;
                        if (stop > end) stop = end;
                        if (start < stop) result.Add(new(Utc(start), Utc(stop)));
                    }
                }
            }
        }
        var merged = new List<ExperienceWindow>();
        foreach (var w in result.OrderBy(w => w.StartUtc))
        {
            if (merged.Count > 0 && merged[^1].EndUtc >= w.StartUtc)
                merged[^1] = new(merged[^1].StartUtc, merged[^1].EndUtc > w.EndUtc ? merged[^1].EndUtc : w.EndUtc);
            else merged.Add(w);
        }
        Validate(merged, expiry, false);
        return merged;
    }
}
