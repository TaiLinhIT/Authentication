using LicenseGenerator.Interop;
using System.Windows;
using System.Windows.Media;

namespace LicenseGenerator.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmLabel, bool danger)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        Title = title;
        DialogTitle.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;

        if (danger)
        {
            IconGlyph.Text = "\uE7BA";
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            IconBadge.Background = (Brush)FindResource("DangerSoftBrush");
            ConfirmButton.Style = (Style)FindResource("DangerButton");
        }
    }

    public static bool Show(Window? owner, string title, string message, string confirmLabel, bool danger)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel, danger);
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
