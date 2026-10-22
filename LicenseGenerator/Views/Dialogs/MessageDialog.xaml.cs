using LicenseGenerator.Interop;
using System.Windows;

namespace LicenseGenerator.Views.Dialogs;

public partial class MessageDialog : Window
{
    private MessageDialog(string title, string message, bool isError)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        Title = title;
        DialogTitle.Text = title;
        MessageText.Text = message;
        IconGlyph.Text = isError ? "\uEA39" : "\uE946";
        IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource(
            isError ? "DangerBrush" : "AccentHoverBrush");
    }

    public static void Show(Window? owner, string title, string message, bool isError)
    {
        var dialog = new MessageDialog(title, message, isError);
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        dialog.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
