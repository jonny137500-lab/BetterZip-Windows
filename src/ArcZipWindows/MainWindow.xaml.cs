using System.IO;
using Microsoft.Win32;
using System.IO.Compression;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ArcZipWindows;

public partial class MainWindow : Window
{
    private readonly ZipArchiveService service = new();
    private readonly LocalApiServer apiServer;
    private string? archivePath;
    private string? pendingEditArchivePath;
    private string? pendingEditEntryName;
    private string? pendingEditFile;

    public MainWindow()
    {
        InitializeComponent();
        apiServer = new LocalApiServer(service);
        Closed += async (_, _) => { await apiServer.StopAsync(); CleanupPendingEdit(); };
        Loaded += (_, _) =>
        {
            var startupArchive = Environment.GetCommandLineArgs().Skip(1)
                .FirstOrDefault(arg => File.Exists(arg) && ZipArchiveService.CanReadFormat(arg));
            if (startupArchive is not null) TryOpenArchive(startupArchive);
        };
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Supported archives|*.zip;*.7z;*.rar;*.tar;*.tgz;*.tar.gz;*.tar.bz2;*.tar.xz;*.ar;*.arj;*.ace;*.lzw|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        TryOpenArchive(dialog.FileName);
    }

    private void TryOpenArchive(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Archive file not found.", path);
            if (!ZipArchiveService.CanReadFormat(path))
                throw new NotSupportedException("This archive format is not supported.");
            archivePath = Path.GetFullPath(path);
            var entries = service.ListAny(archivePath);
            ArchiveLabel.Text = archivePath;
            EntriesGrid.ItemsSource = entries.Where(x => x.Name.Contains(SearchBox.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            UpdateArchiveCapabilities();
            StatusText.Text = $"Opened archive · {entries.Count} entries";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return;
        if (existing.Length == 1 && File.Exists(existing[0]) &&
            string.Equals(Path.GetExtension(existing[0]), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            TryOpenArchive(existing[0]);
            return;
        }
        if (!RequireArchive()) return;
        try
        {
            service.AddPaths(archivePath!, existing);
            RefreshEntries();
            StatusText.Text = $"Added {existing.Length} dropped item(s)";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RefreshEntries()
    {
        if (archivePath is null) return;
        var entries = service.ListAny(archivePath)
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
        var save = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip|7-Zip archive (*.7z)|*.7z|TAR archive (*.tar)|*.tar|TAR.GZ archive (*.tar.gz)|*.tar.gz", DefaultExt = ".zip", FileName = "Archive.zip", AddExtension = true };
        if (save.ShowDialog() != true) return;
        try
        {
            service.CreateAny(save.FileName, files.FileNames, overwrite: true);
            archivePath = save.FileName;
            ArchiveLabel.Text = archivePath;
            UpdateArchiveCapabilities();
            RefreshEntries();
            StatusText.Text = "Archive created";
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
        try { service.ExtractEntriesAny(archivePath!, selected, dialog.FolderName); StatusText.Text = "Extraction complete"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExtractAll_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var dialog = new OpenFolderDialog { Title = "Choose extraction folder" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var names = service.ListAny(archivePath!).Select(x => x.Name).ToList();
            service.ExtractEntriesAny(archivePath!, names, dialog.FolderName);
            StatusText.Text = $"Extracted {names.Count} entries";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var path = archivePath!;
        try
        {
            StatusText.Text = "Verifying archive data and CRC checks…";
            var count = await Task.Run(() => service.VerifyAny(path));
            StatusText.Text = $"Verified {count} file entries; no corruption detected";
            MessageBox.Show($"Verified {count} file entries. No corruption was detected.", "ZIP verification", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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


    private void EditExternally_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var row = SelectedRows().FirstOrDefault();
        if (row is null) { MessageBox.Show("Select one file to edit."); return; }
        if (row.Kind == "Folder") { MessageBox.Show("Select a file, not a folder."); return; }
        if (pendingEditFile is not null && File.Exists(pendingEditFile))
        {
            var answer = MessageBox.Show("There is already an unsaved external edit. Discard it and open another file?", "Pending edit",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            CleanupPendingEdit();
        }
        try
        {
            var editDirectory = Path.Combine(Path.GetTempPath(), "ArcZipWindows", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(editDirectory);
            var extension = Path.GetExtension(row.Name);
            var tempFile = Path.Combine(editDirectory, "edit" + extension);
            service.ExportEntryToFile(archivePath!, row.Name, tempFile);
            Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
            pendingEditArchivePath = archivePath;
            pendingEditEntryName = row.Name;
            pendingEditFile = tempFile;
            SaveBackButton.IsEnabled = true;
            StatusText.Text = $"Editing {row.Name} externally. Save in that app, then click Save back to ZIP.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SaveBack_Click(object sender, RoutedEventArgs e)
    {
        if (pendingEditArchivePath is null || pendingEditEntryName is null || pendingEditFile is null || !File.Exists(pendingEditFile))
        {
            MessageBox.Show("No pending external edit was found.");
            CleanupPendingEdit();
            return;
        }
        try
        {
            service.ReplaceEntry(pendingEditArchivePath, pendingEditEntryName, pendingEditFile);
            if (string.Equals(archivePath, pendingEditArchivePath, StringComparison.OrdinalIgnoreCase)) RefreshEntries();
            StatusText.Text = $"Saved changes to {pendingEditEntryName}";
            CleanupPendingEdit();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void CleanupPendingEdit()
    {
        var file = pendingEditFile;
        pendingEditArchivePath = null;
        pendingEditEntryName = null;
        pendingEditFile = null;
        if (SaveBackButton is not null) SaveBackButton.IsEnabled = false;
        if (file is null) return;
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch { }
    }

    private void UpdateArchiveCapabilities()
    {
        var writable = archivePath is not null && ZipArchiveService.CanModifyFormat(archivePath);
        AddButton.IsEnabled = writable;
        RenameButton.IsEnabled = writable;
        DeleteButton.IsEnabled = writable;
        EditExternallyButton.IsEnabled = writable;
        SaveBackButton.IsEnabled = writable && pendingEditFile is not null && File.Exists(pendingEditFile);
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
