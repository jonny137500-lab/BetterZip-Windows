using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArcZipWindows;

public sealed class ArchivePreviewWindow : Window
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".toml",
        ".cs", ".py", ".js", ".ts", ".html", ".css", ".sql", ".sh", ".bat", ".ps1", ".config"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    public ArchivePreviewWindow(string displayName, string filePath)
    {
        Title = $"Preview — {displayName}";
        Width = 900;
        Height = 680;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new TextBlock
        {
            Text = $"{displayName}  ·  {new FileInfo(filePath).Length:N0} bytes",
            FontSize = 13,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var openButton = new Button { Content = "Open in default app", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        openButton.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot open file", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        var closeButton = new Button { Content = "Close", IsDefault = true, IsCancel = true, Padding = new Thickness(14, 7, 14, 7) };
        footer.Children.Add(openButton);
        footer.Children.Add(closeButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var ext = Path.GetExtension(filePath);
        var info = new FileInfo(filePath);
        if (TextExtensions.Contains(ext) && info.Length <= 1_048_576)
        {
            var text = File.ReadAllText(filePath);
            root.Children.Add(new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Padding = new Thickness(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240))
            });
        }
        else if (ImageExtensions.Contains(ext) && info.Length <= 25 * 1024 * 1024)
        {
            var bitmap = new BitmapImage();
            using (var stream = File.OpenRead(filePath))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1400;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = info.Length > 25 * 1024 * 1024
                    ? "Preview is limited to 25 MB for images and 1 MB for text. Use Open in default app to view this file."
                    : "This file type is not rendered in the preview window. Use Open in default app to view it.",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brushes.Gray,
                FontSize = 15,
                Margin = new Thickness(24)
            });
        }

        Content = root;
        Closed += (_, _) =>
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch { }
        };
    }
}
