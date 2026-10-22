using LicenseGenerator.Interop;
using LicenseGenerator.Services;
using LicenseGenerator.ViewModels;
using LicenseLocalization;
using System.Windows;
using System.Windows.Threading;

namespace LicenseGenerator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        LanguageManager.Initialize();

        try
        {
            var services = new AppServices();
            var window = new MainWindow(services);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LanguageManager.Format("S_StudioStartupError", ex.Message),
                LanguageManager.T("S_StartupErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            LanguageManager.Format("S_UnexpectedError", e.Exception.Message),
            LanguageManager.T("S_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
