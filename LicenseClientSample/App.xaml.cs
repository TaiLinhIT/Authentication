using LicenseClientSample.Models;
using LicenseClientSample.Security;
using LicenseClientSample.Services;
using LicenseClientSample.Views;
using LicenseLocalization;
using LicenseShared;
using System.Windows;
using System.Windows.Threading;

namespace LicenseClientSample;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandledException;
        LanguageManager.Initialize();

        try
        {
            var config = ClientConfig.Load();
            var publicKeys = PublicKeyProvider.LoadDefault();
            var validator = new LicenseValidator(config, publicKeys);

            var result = validator.ValidateInstalled(DateTimeOffset.UtcNow);

            if (result.IsValid)
            {
                ShowMainWindow(config, validator, result);
                return;
            }

            var activation = new ActivationWindow(config, validator, result);
            if (activation.ShowDialog() == true && activation.ActivatedLicense is not null)
                ShowMainWindow(config, validator, activation.ActivatedLicense);
            else
                Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LanguageManager.Format("S_StartupError", ex.Message),
                LanguageManager.T("S_StartupErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowMainWindow(
        ClientConfig config, LicenseValidator validator, LicenseValidationResult result)
    {
        var window = new MainWindow(config, validator, result);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            LanguageManager.Format("S_UnexpectedError", e.Exception.Message),
            "WarehouseLabelCompareMaterial", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
