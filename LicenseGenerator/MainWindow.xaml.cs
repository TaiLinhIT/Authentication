using LicenseGenerator.Interop;
using LicenseGenerator.Services;
using LicenseGenerator.ViewModels;
using LicenseLocalization;
using System.Windows;

namespace LicenseGenerator;

public partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);
        DataContext = new MainViewModel(services);
    }

    private void Language_Click(object sender, RoutedEventArgs e) => LanguageManager.Toggle();
}
