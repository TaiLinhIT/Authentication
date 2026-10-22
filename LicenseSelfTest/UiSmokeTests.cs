using LicenseLocalization;
using LicenseShared;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LicenseSelfTest;

/// <summary>
/// Exercises the WPF surface: loads both themes and language catalogs, materializes the real
/// windows and views, and switches English → Vietnamese → English to prove that DynamicResource
/// text, code-built labels and the language toggle all update at runtime.
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
                CheckTheme(check, "WarehouseLabelCompareMaterial", "client");
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

            var payload = new LicensePayload
            {
                ProductId = "X",
                LicenseType = LicenseType.Days90,
                NotBeforeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ExpiresAtUtc = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var expired = LicenseValidationResult.Fail(LicenseStatus.Expired, "expired", payload);

            check("client type label starts in English",
                LicenseClientSample.Localization.L.TypeName(LicenseType.Days90) == "90 days",
                LicenseClientSample.Localization.L.TypeName(LicenseType.Days90));
            check("client status message starts in English",
                LicenseClientSample.Localization.L.Describe(expired, "X").Contains("expired", StringComparison.Ordinal),
                LicenseClientSample.Localization.L.Describe(expired, "X"));

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
            check("client type label switches at runtime",
                LicenseClientSample.Localization.L.TypeName(LicenseType.Days90) == "90 ngày",
                LicenseClientSample.Localization.L.TypeName(LicenseType.Days90));
            check("client status message switches at runtime",
                LicenseClientSample.Localization.L.Describe(expired, "X").Contains("hết hạn", StringComparison.Ordinal),
                LicenseClientSample.Localization.L.Describe(expired, "X"));

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
            check("client status message switches back at runtime",
                LicenseClientSample.Localization.L.Describe(expired, "X").Contains("expired", StringComparison.Ordinal),
                LicenseClientSample.Localization.L.Describe(expired, "X"));

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

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WarehouseLabelCompareMaterial;component/Theme.xaml")
            });

            var config = new LicenseClientSample.Models.ClientConfig
            {
                ProductId = "SelfTestProduct",
                ProductName = "Self Test",
                Vendor = "Vendor",
                Version = "1.0.0"
            };
            var validator = new LicenseClientSample.Services.LicenseValidator(
                config, LicenseClientSample.Security.PublicKeyProvider.LoadDefault());

            var activation = new LicenseClientSample.Views.ActivationWindow(
                config, validator, LicenseValidationResult.Fail(LicenseStatus.Missing, "missing"));

            check("client activation window materializes", activation.Content is not null, null);

            var activationToggle = FindLogical<Button>(activation)
                .FirstOrDefault(b => b.Content as string == "VI");
            check("client language toggle is present in English", activationToggle is not null, null);

            string englishTitle = activation.Title;
            LanguageManager.SetLanguage(AppLanguage.Vietnamese);
            check("client activation window title switches",
                activation.Title != englishTitle, activation.Title);
            check("client language toggle switches to EN",
                activationToggle?.Content as string == "EN", activationToggle?.Content as string);

            LanguageManager.SetLanguage(AppLanguage.English);
            check("client activation window title switches back",
                activation.Title == englishTitle, activation.Title);
            check("client language toggle switches back to VI",
                activationToggle?.Content as string == "VI", activationToggle?.Content as string);

            var now = DateTimeOffset.UtcNow;
            var payload = new LicensePayload
            {
                LicenseId = "WLC-TEST-000001",
                ProductId = config.ProductId,
                ProductName = config.ProductName,
                CustomerName = "Test Customer",
                StationName = "Test Station",
                DeviceId = "DEV-0123456789ABCDEF",
                BindingMode = DeviceBindingMode.MachineFingerprint,
                LicenseType = LicenseType.Days90,
                IssuedAtUtc = now,
                NotBeforeUtc = now.AddMinutes(-1),
                ExpiresAtUtc = now.AddDays(90),
                KeyId = "TESTKEY"
            };

            var clientMain = new LicenseClientSample.Views.MainWindow(
                config, validator, LicenseValidationResult.Valid(payload));

            check("client main window materializes", clientMain.Content is not null, null);

            var mainToggle = FindLogical<Button>(clientMain)
                .FirstOrDefault(b => b.Content as string == "VI");
            check("client main language toggle is present in English", mainToggle is not null, null);

            var pill = FindLogical<ContentControl>(clientMain)
                .FirstOrDefault(c => c.Content as string == "License active");
            check("client license pill starts in English", pill is not null, null);

            LanguageManager.SetLanguage(AppLanguage.Vietnamese);
            check("client main language toggle switches to EN",
                mainToggle?.Content as string == "EN", mainToggle?.Content as string);
            check("client license pill switches to Vietnamese",
                pill?.Content as string == "License đang hiệu lực", pill?.Content as string);

            LanguageManager.SetLanguage(AppLanguage.English);
            check("client main language toggle switches back to VI",
                mainToggle?.Content as string == "VI", mainToggle?.Content as string);
            check("client license pill switches back to English",
                pill?.Content as string == "License active", pill?.Content as string);

            var info = new LicenseClientSample.Views.LicenseInfoDialog(
                config, validator, LicenseValidationResult.Valid(payload));
            check("client license information dialog materializes", info.Content is not null, null);
            try { info.Close(); } catch (InvalidOperationException) { }

            try { clientMain.Close(); } catch (InvalidOperationException) { }

            try { activation.Close(); } catch (InvalidOperationException) { }
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
