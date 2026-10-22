using LicenseGenerator.Interop;
using System.Windows;

namespace LicenseGenerator.Views.Dialogs;

public partial class PasswordPromptDialog : Window
{
    private readonly Func<string, string?> _validate;

    private PasswordPromptDialog(string message, Func<string, string?> validate)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);

        _validate = validate;
        MessageText.Text = message;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string Password => PasswordInput.Password;

    public static string? Show(Window? owner, string message, Func<string, string?> validate)
    {
        var dialog = new PasswordPromptDialog(message, validate);

        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        string? error = _validate(PasswordInput.Password);
        if (error is null)
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Focus();
        PasswordInput.SelectAll();
    }
}
