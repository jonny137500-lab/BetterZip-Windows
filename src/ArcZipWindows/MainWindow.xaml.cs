using System.IO;
using Microsoft.Win32;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;

namespace ArcZipWindows;

public partial class MainWindow : Window
{
    private readonly ZipArchiveService service = new();
    private readonly LocalApiServer apiServer;
    private string? archivePath;

    public MainWindow()
    {
        InitializeComponent();
        apiServer = new LocalApiServer(service);
        Closed += async (_, _) => await apiServer.StopAsync();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var _ = ZipFile.OpenRead(dialog.FileName);
            archivePath = dialog.FileName;
            ArchiveLabel.Text = archivePath;
            RefreshEntries();
            StatusText.Text = "Archive opened";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RefreshEntries()
    {
        if (archivePath is null) return;
        var entries = service.List(archivePath)
            .Where(x => x.Name.Contains(SearchBox.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
        EntriesGrid.ItemsSource = entries;
        StatusText.Text = $"{entries.Count} entries";
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) RefreshEntries();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var files = new OpenFileDialog { Multiselect = true, Title = "Choose files to add to a new ZIP" };
        if (files.ShowDialog() != true) return;
        var save = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip", DefaultExt = ".zip", FileName = "Archive.zip" };
        if (save.ShowDialog() != true) return;
        try
        {
            service.Create(save.FileName, files.FileNames, overwrite: true);
            archivePath = save.FileName;
            ArchiveLabel.Text = archivePath;
            RefreshEntries();
            StatusText.Text = "ZIP created";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to add" };
        if (dialog.ShowDialog() != true) return;
        try { service.AddFiles(archivePath!, dialog.FileNames); RefreshEntries(); StatusText.Text = "Files added"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var selected = SelectedRows().Select(x => x.Name).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select one or more entries first."); return; }
        var dialog = new OpenFolderDialog { Title = "Choose extraction folder" };
        if (dialog.ShowDialog() != true) return;
        try { service.ExtractEntries(archivePath!, selected, dialog.FolderName); StatusText.Text = "Extraction complete"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var selected = SelectedRows().Select(x => x.Name).ToList();
        if (selected.Count == 0) return;
        if (MessageBox.Show($"Delete {selected.Count} selected entr(y/ies)?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { service.DeleteEntries(archivePath!, selected); RefreshEntries(); StatusText.Text = "Entries deleted"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var row = SelectedRows().FirstOrDefault();
        if (row is null) { MessageBox.Show("Select one entry to rename."); return; }
        var dialog = new RenameWindow(row.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { service.RenameEntry(archivePath!, row.Name, dialog.NewName); RefreshEntries(); StatusText.Text = "Entry renamed"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Api_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (apiServer.IsRunning)
            {
                apiServer.StopAsync().GetAwaiter().GetResult();
                ApiButton.Content = "API: Start";
                StatusText.Text = "Local API stopped; token removed";
            }
            else
            {
                apiServer.Start();
                ApiButton.Content = "API: Stop";
                StatusText.Text = $"API active at {apiServer.BaseUrl} · token: {apiServer.TokenPath}";
                MessageBox.Show($"Local API is enabled.\n\nBase URL: {apiServer.BaseUrl}\nToken file: {apiServer.TokenPath}\n\nOnly local apps can connect. Keep the token private. The API stops when this app closes.", "ArcZip Windows API", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private IEnumerable<ArchiveEntryRow> SelectedRows() => EntriesGrid.SelectedItems.Cast<ArchiveEntryRow>();
    private bool RequireArchive()
    {
        if (archivePath is not null && File.Exists(archivePath)) return true;
        MessageBox.Show("Open a ZIP archive first.");
        return false;
    }
    private static void ShowError(Exception ex) => MessageBox.Show(ex.Message, "ArcZip Windows", MessageBoxButton.OK, MessageBoxImage.Error);
}
