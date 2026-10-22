using LicenseGenerator.Interop;
using LicenseGenerator.Services;
using LicenseGenerator.ViewModels;
using LicenseShared;
using System.Windows;

namespace LicenseGenerator.Views.Dialogs;

public partial class IssueLicenseDialog : Window
{
    public IssueLicenseDialog(AppServices services, ProductDefinition? product, LicenseRecord? renewFrom)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        ViewModel = new IssueLicenseViewModel(services, product, renewFrom);
        DataContext = ViewModel;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IssueLicenseViewModel.HasResult) && ViewModel.HasResult)
                Generated = true;
        };
    }

    public IssueLicenseViewModel ViewModel { get; }

    public bool Generated { get; private set; }

    private void Done_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
