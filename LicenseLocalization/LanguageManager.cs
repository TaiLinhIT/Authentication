using System.Globalization;
using System.IO;
using System.Windows;
using LicenseShared;

namespace LicenseLocalization;

public enum AppLanguage
{
    English,
    Vietnamese,
    Chinese,
    Japanese
}

/// <summary>
/// Runtime language switch for both WPF applications.
///
/// All user-visible strings live in <c>Strings.en.xaml</c> and <c>Strings.vi.xaml</c>.
/// XAML uses <c>{DynamicResource S_Key}</c>, so replacing the merged dictionary updates
/// every open window immediately. Code-behind and view models use <see cref="T"/> and
/// <see cref="Format"/>.
/// </summary>
public static class LanguageManager
{
    private const string LanguageFileName = "ui-language.txt";

    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    /// <summary>Raised after the dictionaries have been swapped. Use it to refresh computed labels.</summary>
    public static event EventHandler? LanguageChanged;

    public static bool IsVietnamese => Current == AppLanguage.Vietnamese;

    /// <summary>Loads the saved language (English by default) and installs its dictionary.</summary>
    public static void Initialize()
    {
        Current = ReadSavedLanguage();
        Apply(Current);
    }

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;

        Current = language;
        Apply(language);
        TrySave(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Toggle() =>
        SetLanguage((AppLanguage)(((int)Current + 1) % Enum.GetValues<AppLanguage>().Length));

    public static string Code(AppLanguage language) => language switch
    {
        AppLanguage.Vietnamese => "vi",
        AppLanguage.Chinese => "zh",
        AppLanguage.Japanese => "ja",
        _ => "en"
    };

    /// <summary>Returns the string for <paramref name="key"/> in the active language.</summary>
    public static string T(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is string value) return value;
        }
        catch (InvalidOperationException)
        {
            // Resource lookup outside the UI thread; fall back to the key.
        }

        return key;
    }

    /// <summary>Formats a localized string that contains {0}, {1}, ... placeholders.</summary>
    public static string Format(string key, params object?[] args)
    {
        string template = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static void Apply(AppLanguage language)
    {
        var app = Application.Current;
        if (app is null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Apply(language));
            return;
        }

        var merged = app.Resources.MergedDictionaries;

        for (int i = merged.Count - 1; i >= 0; i--)
        {
            string? source = merged[i].Source?.OriginalString;
            if (source is not null && source.Contains("Strings.", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        string code = Code(language);
        merged.Add(new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/LicenseLocalization;component/Strings.{code}.xaml",
                UriKind.Absolute)
        });
    }

    private static string LanguageFilePath => Path.Combine(LicensePaths.Root, LanguageFileName);

    private static AppLanguage ReadSavedLanguage()
    {
        try
        {
            if (!File.Exists(LanguageFilePath)) return AppLanguage.English;

            string text = File.ReadAllText(LanguageFilePath).Trim();
            return Enum.GetValues<AppLanguage>().FirstOrDefault(language =>
                text.Equals(Code(language), StringComparison.OrdinalIgnoreCase)
                || text.Equals(language.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppLanguage.English;
        }
    }

    private static void TrySave(AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(LicensePaths.Root);
            File.WriteAllText(
                LanguageFilePath,
                Code(language));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only profile must not break the language switch for this session.
        }
    }
}
