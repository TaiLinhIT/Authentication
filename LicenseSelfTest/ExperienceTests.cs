using LicenseShared;

namespace LicenseSelfTest;

internal static class ExperienceTests
{
    public static void Run(Action<string, bool, string?> check)
    {
        void Test(string name, bool ok) => check("Experience: " + name, ok, null);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +7", TimeSpan.FromHours(7), "Test +7", "Test +7");
        var date = new DateOnly(2026, 10, 23);
        var time = new TimeOnly(16, 0);
        DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7));
        var expiry = At(23, 16);
        var hourly = ExtraExperience.Build(date, time, zone, new[] { 0, 23 }, Array.Empty<DateOnly>());
        Test("first hour", ExtraExperience.Allows(hourly, At(23, 16, 30)));
        Test("unselected hour closed", !ExtraExperience.Allows(hourly, At(23, 17)));
        Test("last hour", ExtraExperience.Allows(hourly, At(24, 15)));
        Test("24 hour limit exclusive", !ExtraExperience.Allows(hourly, At(24, 16)));
        var days = new[] { date.AddDays(1), date.AddMonths(1) };
        var daily = ExtraExperience.Build(date, time, zone, Array.Empty<int>(), days);
        Test("selected whole day", ExtraExperience.Allows(daily, At(24, 0)) && ExtraExperience.Allows(daily, At(24, 23, 59)));
        Test("unselected day", !ExtraExperience.Allows(daily, At(25, 16)));
        Test("last date included", ExtraExperience.Allows(daily, new(2026, 11, 23, 23, 59, 0, TimeSpan.FromHours(7))));
        Test("month boundary", !ExtraExperience.Allows(daily, new(2026, 11, 24, 0, 0, 0, TimeSpan.FromHours(7))));
        var both = ExtraExperience.Build(date, time, zone, new[] { 0, 8 }, days);
        Test("intersection selected day/hour", ExtraExperience.Allows(both, At(24, 16)));
        Test("intersection excludes other hours", !ExtraExperience.Allows(both, At(24, 17)));
        Test("intersection excludes expiry date", !ExtraExperience.Allows(both, At(23, 16, 30)));
        Test("wrapped hour on selected date", ExtraExperience.Allows(both, At(24, 0)));
        var partial = ExtraExperience.Build(date, new TimeOnly(16, 30), zone, new[] { 7 }, new[] { date.AddDays(1) });
        Test("midnight crossing clipped to selected day", ExtraExperience.Allows(partial, At(24, 0, 15)) && ExtraExperience.Allows(partial, At(24, 23, 45)) && !ExtraExperience.Allows(partial, At(25, 0, 15)));
        Test("empty schedule", ExtraExperience.Build(date, time, zone, Array.Empty<int>(), Array.Empty<DateOnly>()).Count == 0);
        Test("leap month end", ExtraExperience.Build(new(2028, 1, 31), time, zone, Array.Empty<int>(), new[] { new DateOnly(2028, 2, 29) }).Count == 1);
        var p = new LicensePayload { ProductId = "test", DeviceId = "device", BindingMode = DeviceBindingMode.ManualDeviceId,
            ExpiresAtUtc = expiry, NotBeforeUtc = expiry.AddDays(-1), IssuedAtUtc = expiry.AddDays(-1), ExtraWindows = both };
        var encoded = LicenseCodec.SerializePayload(p);
        var decoded = LicenseCodec.DeserializePayload(encoded);
        Test("v2 round trip", encoded[0] == 2 && decoded.ExtraWindows.SequenceEqual(both));
        Test("validator permits intersection", LicenseRules.Evaluate(decoded, "test", "", "device", At(24, 16, 30)).IsValid);
        Test("validator rejects outside", LicenseRules.Evaluate(decoded, "test", "", "device", At(24, 18)).Status == LicenseStatus.Expired);
        Test("binding still enforced", LicenseRules.Evaluate(decoded, "test", "", "other", At(24, 16, 30)).Status == LicenseStatus.WrongMachine);
        Test("product still enforced", LicenseRules.Evaluate(decoded, "other", "", "device", At(24, 16, 30)).Status == LicenseStatus.WrongProduct);
        p.ExtraWindows = new();
        Test("legacy v1 preserved", LicenseCodec.SerializePayload(p)[0] == 1);
        Test("legacy still expires", LicenseRules.Evaluate(p, "test", "", "device", At(24, 16)).Status == LicenseStatus.Expired);
        try { ExtraExperience.Build(date, time, zone, new[] { 24 }, days); Test("invalid hour rejected", false); }
        catch (ArgumentException) { Test("invalid hour rejected", true); }
        try { ExtraExperience.Build(date, time, zone, new[] { 0 }, new[] { date.AddMonths(1).AddDays(1) }); Test("invalid date rejected", false); }
        catch (ArgumentException) { Test("invalid date rejected", true); }
    }
}
