using LicenseLocalization;
using LicenseShared;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LicenseSelfTest;

/// <summary>
/// Exercises the WPF surface: loads the issuer theme and language catalogs, materializes the
/// real issuer windows and views, and switches English → Vietnamese → English to prove that
/// DynamicResource text, code-built labels and the language toggle all update at runtime.
/// </summary>
internal static class UiSmokeTests
{
    public static void Run(Action<string, bool, string?> check)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                CheckTheme(check, "LicenseGenerator", "generator");
                CheckLocalization(check);
                CheckWindows(check);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
            check("UI theme resources load", false, failure.GetType().Name + ": " + failure.Message);
    }

    private static void CheckTheme(Action<string, bool, string?> check, string assembly, string label)
    {
        try
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{assembly};component/Theme.xaml")
            };

            object? style = dictionary["DialogWindow"];
            check($"{label} theme provides DialogWindow", style is Style, dictionary.Count + " resources");

            check($"{label} theme provides WindowBrush", dictionary["WindowBrush"] is not null, null);
        }
        catch (Exception ex)
        {
            check($"{label} theme loads", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void CheckLocalization(Action<string, bool, string?> check)
    {
        try
        {
            var english = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LicenseLocalization;component/Strings.en.xaml")
            };
            var vietnamese = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LicenseLocalization;component/Strings.vi.xaml")
            };

            var enKeys = english.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var viKeys = vietnamese.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k, StringComparer.Ordinal).ToArray();

            check("English catalog has at least 300 keys", enKeys.Length >= 300, enKeys.Length + " keys");
            check("Language catalogs expose the same keys",
                enKeys.SequenceEqual(viKeys, StringComparer.Ordinal),
                $"en={enKeys.Length}, vi={viKeys.Length}");

            var empty = enKeys
                .Where(k => english[k] is not string en || string.IsNullOrWhiteSpace(en)
                            || vietnamese[k] is not string vi || string.IsNullOrWhiteSpace(vi))
                .ToArray();
            check("Every localized string has text in both languages", empty.Length == 0,
                empty.Length == 0 ? null : string.Join(", ", empty.Take(5)));

            check("Placeholder text is preserved",
                english["S_ActivationWindowTitle"] as string == "{0} — Activation"
                && vietnamese["S_ActivationWindowTitle"] as string == "{0} — Kích hoạt",
                english["S_ActivationWindowTitle"] as string);

            var multiline = english["S_DeleteProductMessage"] as string;
            check("Multi-line text keeps its newlines",
                multiline?.Contains('\n') == true,
                multiline is null
                    ? "missing"
                    : multiline.Length + " chars: " +
                      multiline.Replace("\r", "\\r").Replace("\n", "\\n"));

            LanguageManager.Initialize();
            LanguageManager.SetLanguage(AppLanguage.English);

            var block = new TextBlock();
            block.SetResourceReference(TextBlock.TextProperty, "S_NavDashboard");
            var host = new Window { Content = block };

            var services = new LicenseGenerator.Services.AppServices();
            var vm = new LicenseGenerator.ViewModels.MainViewModel(services);

            check("issuer navigation starts in English",
                vm.NavItems[0].Title == "Dashboard", vm.NavItems[0].Title);
            check("issuer stat cards start in English",
                vm.Dashboard.Stats.Count > 0 && vm.Dashboard.Stats[0].Label == "PRODUCTS",
                vm.Dashboard.Stats.Count > 0 ? vm.Dashboard.Stats[0].Label : "no stats");

            check("DynamicResource resolves English at runtime",
                block.Text == "Dashboard", block.Text);

            LanguageManager.SetLanguage(AppLanguage.Vietnamese);
            check("DynamicResource switches to Vietnamese at runtime",
                block.Text == "Tổng quan", block.Text);
            check("Code lookup switches to Vietnamese at runtime",
                LanguageManager.T("S_NavDashboard") == "Tổng quan",
                LanguageManager.T("S_NavDashboard"));
            check("Issuer navigation switches at runtime",
                vm.NavItems[0].Title == "Tổng quan", vm.NavItems[0].Title);
            check("Issuer stat cards switch at runtime",
                vm.Dashboard.Stats[0].Label == "SẢN PHẨM", vm.Dashboard.Stats[0].Label);
            check("Issuer computed counts switch at runtime",
                vm.Products.CountText.Contains("sản phẩm", StringComparison.Ordinal),
                vm.Products.CountText);

            LanguageManager.SetLanguage(AppLanguage.English);
            check("DynamicResource switches back to English at runtime",
                block.Text == "Dashboard", block.Text);
            check("Issuer navigation switches back at runtime",
                vm.NavItems[0].Title == "Dashboard", vm.NavItems[0].Title);
            check("Issuer stat cards switch back at runtime",
                vm.Dashboard.Stats[0].Label == "PRODUCTS", vm.Dashboard.Stats[0].Label);
            check("Issuer computed counts switch back at runtime",
                vm.Products.CountText.Contains("product", StringComparison.Ordinal),
                vm.Products.CountText);

            host.Close();
        }
        catch (Exception ex)
        {
            check("Localization catalogs load", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Materializes the real application windows so XAML resource lookups are exercised.</summary>
    private static void CheckWindows(Action<string, bool, string?> check)
    {
        try
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LicenseGenerator;component/Theme.xaml")
            });

            LanguageManager.SetLanguage(AppLanguage.English);

            var services = new LicenseGenerator.Services.AppServices();
            var issuer = new LicenseGenerator.MainWindow(services);
            check("issuer main window materializes", issuer.Content is not null, null);
            check("issuer main window title resolves",
                issuer.Title == "Warehouse License Studio", issuer.Title);

            MaterializeView<LicenseGenerator.Views.DashboardView>(check, "dashboard");
            MaterializeView<LicenseGenerator.Views.ProductsView>(check, "products");
            MaterializeView<LicenseGenerator.Views.LicensesView>(check, "licenses");
            MaterializeView<LicenseGenerator.Views.SettingsView>(check, "settings");

            var issuerToggle = FindLogical<Button>(issuer)
                .FirstOrDefault(b => b.Content as string == "VI");
            check("issuer language toggle is present in English", issuerToggle is not null, null);

            LanguageManager.SetLanguage(AppLanguage.Vietnamese);
            check("issuer language toggle switches to EN",
                issuerToggle?.Content as string == "EN", issuerToggle?.Content as string);
            LanguageManager.SetLanguage(AppLanguage.English);
            check("issuer language toggle switches back to VI",
                issuerToggle?.Content as string == "VI", issuerToggle?.Content as string);

            try { issuer.Close(); } catch (InvalidOperationException) { }
        }
        catch (Exception ex)
        {
            check("application windows materialize", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject) continue;

            foreach (T nested in FindLogical<T>(dependencyObject))
                yield return nested;
        }
    }

    private static void MaterializeView<T>(Action<string, bool, string?> check, string name)
        where T : FrameworkElement, new()
    {
        try
        {
            _ = new T();
            check($"issuer {name} view materializes", true, null);
        }
        catch (Exception ex)
        {
            check($"issuer {name} view materializes", false, ex.GetType().Name + ": " + ex.Message);
        }
    }
}
