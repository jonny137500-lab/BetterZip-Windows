using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcZipWindows;

public sealed class PasswordPromptWindow : Window
{
    private readonly PasswordBox passwordBox = new();
    public string Password => passwordBox.Password;

    public PasswordPromptWindow(string archiveName)
    {
        Title = "Archive password";
        Width = 420;
        Height = 190;
        MinWidth = 380;
        MinHeight = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Enter the password for {archiveName}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 14
        });
        passwordBox.Margin = new Thickness(0, 0, 0, 14);
        passwordBox.MinWidth = 340;
        passwordBox.Height = 32;
        panel.Children.Add(passwordBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 86, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var open = new Button { Content = "Open", IsDefault = true, MinWidth = 86, Padding = new Thickness(12, 6, 12, 6) };
        open.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => passwordBox.Focus();
    }
}
