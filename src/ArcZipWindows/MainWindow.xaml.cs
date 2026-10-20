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
    private string? currentArchivePassword;
    private bool isBusy;
    private CancellationTokenSource? operationCts;
    private bool nestedReadOnly;
    private string currentFolderPrefix = "";
    private readonly Stack<ArchiveNavigationState> navigationStack = new();
    private readonly HashSet<string> nestedTempFiles = new(StringComparer.OrdinalIgnoreCase);
    private sealed record ArchiveNavigationState(string ArchivePath, string? Password, string FolderPrefix, string DisplayLabel, string EntryName, bool ReadOnly);

    public MainWindow()
    {
        InitializeComponent();
        apiServer = new LocalApiServer(service);
        Closed += async (_, _) => { await apiServer.StopAsync(); CleanupPendingEdit(); ClearNestedArchives(); };
        Loaded += (_, _) =>
        {
            var startupArchive = Environment.GetCommandLineArgs().Skip(1)
                .FirstOrDefault(arg => File.Exists(arg) && ZipArchiveService.CanReadFormat(arg));
            if (startupArchive is not null) TryOpenArchive(startupArchive);
        };
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Supported archives|*.zip;*.7z;*.7z.001;*.rar;*.tar;*.tgz;*.tar.gz;*.tar.bz2;*.tar.xz;*.ar;*.arj;*.ace;*.lzw|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        TryOpenArchive(dialog.FileName);
    }

    private bool TryOpenArchive(string path, bool resetNavigation = true)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Archive file not found.", path);
            if (!ZipArchiveService.CanReadFormat(path))
                throw new NotSupportedException("This archive format is not supported.");
            var fullPath = Path.GetFullPath(path);
            string? password = null;
            IReadOnlyList<ArchiveEntryRow> entries;
            try
            {
                entries = service.ListAny(fullPath);
                if (entries.Any(entry => entry.IsEncrypted))
                {
                    var prompt = new PasswordPromptWindow(Path.GetFileName(fullPath)) { Owner = this };
                    if (prompt.ShowDialog() != true) return false;
                    password = prompt.Password;
                    entries = service.ListAny(fullPath, password);
                }
            }
            catch
            {
                var ext = Path.GetExtension(fullPath);
                if (!new[] { ".zip", ".7z", ".rar" }.Contains(ext, StringComparer.OrdinalIgnoreCase) && !fullPath.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase)) throw;
                var prompt = new PasswordPromptWindow(Path.GetFileName(fullPath)) { Owner = this };
                if (prompt.ShowDialog() != true) return false;
                password = prompt.Password;
                entries = service.ListAny(fullPath, password);
            }
            if (resetNavigation)
            {
                currentFolderPrefix = "";
                nestedReadOnly = false;
                navigationStack.Clear();
                ClearNestedArchives();
            }
            archivePath = fullPath;
            currentArchivePassword = password;
            ArchiveLabel.Text = archivePath;
            ApplyVisibleEntries(entries);
            UpdateArchiveCapabilities();
            StatusText.Text = $"Opened archive · {entries.Count} entries";
            return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
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

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return;
        if (existing.Length == 1 && File.Exists(existing[0]) &&
            ZipArchiveService.CanReadFormat(existing[0]))
        {
            TryOpenArchive(existing[0]);
            return;
        }
        if (!RequireArchive()) return;
        try
        {
            await RunBusyAsync("Adding dropped files and folders…", () => service.AddPaths(archivePath!, existing));
            RefreshEntries();
            StatusText.Text = $"Added {existing.Length} dropped item(s)";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RefreshEntries()
    {
        if (archivePath is null) return;
        var entries = service.ListAny(archivePath, currentArchivePassword);
        ApplyVisibleEntries(entries);
        StatusText.Text = $"{EntriesGrid.Items.Count} entries";
    }

    private void ApplyVisibleEntries(IEnumerable<ArchiveEntryRow> allEntries)
    {
        var visible = new Dictionary<string, ArchiveEntryRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allEntries)
        {
            if (!entry.Name.StartsWith(currentFolderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = entry.Name[currentFolderPrefix.Length..].TrimStart('/');
            if (relative.Length == 0) continue;
            var slash = relative.IndexOf('/');
            if (slash >= 0)
            {
                var folderName = relative[..slash];
                var fullFolder = currentFolderPrefix + folderName + "/";
                if (!visible.ContainsKey(fullFolder))
                    visible[fullFolder] = new ArchiveEntryRow { Name = fullFolder, DisplayName = folderName, Kind = "Folder" };
                continue;
            }
            visible[entry.Name] = new ArchiveEntryRow
            {
                Name = entry.Name,
                DisplayName = relative,
                Kind = entry.Kind,
                IsEncrypted = entry.IsEncrypted,
                Length = entry.Length,
                CompressedLength = entry.CompressedLength,
                Modified = entry.Modified
            };
        }
        EntriesGrid.ItemsSource = visible.Values
            .Where(x => x.DisplayName.Contains(SearchBox.Text ?? "", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Kind == "Folder" ? 0 : 1)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        UpButton.IsEnabled = currentFolderPrefix.Length > 0 || navigationStack.Count > 0;
    }

    private void ClearNestedArchives()
    {
        foreach (var file in nestedTempFiles.ToArray())
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            try
            {
                var directory = Path.GetDirectoryName(file);
                if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch { }
        }
        nestedTempFiles.Clear();
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        if (currentFolderPrefix.Length > 0)
        {
            var trimmed = currentFolderPrefix.TrimEnd('/');
            var slash = trimmed.LastIndexOf('/');
            currentFolderPrefix = slash >= 0 ? trimmed[..(slash + 1)] : "";
            RefreshEntries();
            return;
        }
        if (navigationStack.Count == 0) return;
        var current = archivePath;
        var state = navigationStack.Peek();
        if (current is not null && nestedTempFiles.Contains(current) && !state.ReadOnly)
        {
            try
            {
                await RunBusyAsync("Saving nested archive back to its parent…",
                    () => service.ReplaceEntry(state.ArchivePath, state.EntryName, current));
            }
            catch (Exception ex) { ShowError(ex); return; }
        }
        navigationStack.Pop();
        if (current is not null && nestedTempFiles.Remove(current))
        {
            try { if (File.Exists(current)) File.Delete(current); } catch { }
            try
            {
                var directory = Path.GetDirectoryName(current);
                if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch { }
        }
        archivePath = state.ArchivePath;
        currentArchivePassword = state.Password;
        currentFolderPrefix = state.FolderPrefix;
        nestedReadOnly = state.ReadOnly;
        ArchiveLabel.Text = state.DisplayLabel;
        RefreshEntries();
        UpdateArchiveCapabilities();
    }

    private async void EntriesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = SelectedRows().FirstOrDefault();
        if (row is null || archivePath is null) return;
        if (row.Kind == "Folder")
        {
            currentFolderPrefix = row.Name.EndsWith('/') ? row.Name : row.Name + "/";
            SearchBox.Text = "";
            RefreshEntries();
            return;
        }
        if (!ZipArchiveService.CanReadFormat(row.Name)) return;
        var parentPath = archivePath;
        var parentLabel = ArchiveLabel.Text;
        var parentPassword = currentArchivePassword;
        var parentPrefix = currentFolderPrefix;
        var parentReadOnlyBefore = nestedReadOnly;
        var directory = Path.Combine(Path.GetTempPath(), "ArcZipWindows", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var nestedPath = Path.Combine(directory, Path.GetFileName(row.Name));
        try
        {
            await RunBusyAsync("Opening nested archive…", () => service.ExportEntryToFile(parentPath, row.Name, nestedPath, currentArchivePassword));
            var parentReadOnly = nestedReadOnly || !ZipArchiveService.CanModifyFormat(parentPath) || !string.IsNullOrEmpty(currentArchivePassword);
            navigationStack.Push(new ArchiveNavigationState(parentPath, currentArchivePassword, currentFolderPrefix, parentLabel, row.Name, parentReadOnly));
            nestedReadOnly = parentReadOnly;
            nestedTempFiles.Add(nestedPath);
            currentFolderPrefix = "";
            if (!TryOpenArchive(nestedPath, resetNavigation: false))
            {
                navigationStack.Pop();
                nestedTempFiles.Remove(nestedPath);
                try { Directory.Delete(directory, recursive: true); } catch { }
                archivePath = parentPath;
                currentArchivePassword = parentPassword;
                currentFolderPrefix = parentPrefix;
                nestedReadOnly = parentReadOnlyBefore;
                ArchiveLabel.Text = parentLabel;
                RefreshEntries();
                UpdateArchiveCapabilities();
                return;
            }
            ArchiveLabel.Text = $"{parentLabel}  ›  {row.DisplayName}";
            RefreshEntries();
        }
        catch (Exception ex)
        {
            nestedTempFiles.Remove(nestedPath);
            try { Directory.Delete(directory, recursive: true); } catch { }
            archivePath = parentPath;
            currentArchivePassword = parentPassword;
            currentFolderPrefix = parentPrefix;
            nestedReadOnly = parentReadOnlyBefore;
            ArchiveLabel.Text = parentLabel;
            ShowError(ex);
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) RefreshEntries();
    }

    private int SelectedCompressionLevel => CompressionLevelBox.SelectedIndex switch { 0 => 1, 2 => 9, _ => 6 };

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var files = new OpenFileDialog { Multiselect = true, Title = "Choose files for new archive" };
        if (files.ShowDialog() != true) return;
        var save = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip|7-Zip archive (*.7z)|*.7z|TAR archive (*.tar)|*.tar|TAR.GZ archive (*.tar.gz)|*.tar.gz", DefaultExt = ".zip", FileName = "Archive.zip", AddExtension = true };
        if (save.ShowDialog() != true) return;
        try
        {
            await RunBusyAsync("Creating archive…", () => service.CreateFromPathsAny(save.FileName, files.FileNames, overwrite: true, compressionLevel: SelectedCompressionLevel));
            archivePath = save.FileName;
            currentArchivePassword = null;
            currentFolderPrefix = "";
            nestedReadOnly = false;
            navigationStack.Clear();
            ClearNestedArchives();
            ArchiveLabel.Text = archivePath;
            UpdateArchiveCapabilities();
            RefreshEntries();
            StatusText.Text = "Archive created";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void CreateFromFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = new OpenFolderDialog { Title = "Choose a folder to archive" };
        if (folder.ShowDialog() != true) return;
        var folderName = new DirectoryInfo(folder.FolderName).Name;
        var save = new SaveFileDialog
        {
            Filter = "ZIP archive (*.zip)|*.zip|7-Zip archive (*.7z)|*.7z|TAR archive (*.tar)|*.tar|TAR.GZ archive (*.tar.gz)|*.tar.gz",
            DefaultExt = ".zip",
            FileName = folderName + ".zip",
            AddExtension = true
        };
        if (save.ShowDialog() != true) return;
        try
        {
            await RunBusyAsync("Creating archive from folder…", () =>
                service.CreateFromPathsAny(save.FileName, [folder.FolderName], overwrite: true, compressionLevel: SelectedCompressionLevel));
            archivePath = save.FileName;
            currentArchivePassword = null;
            currentFolderPrefix = "";
            nestedReadOnly = false;
            navigationStack.Clear();
            ClearNestedArchives();
            ArchiveLabel.Text = archivePath;
            UpdateArchiveCapabilities();
            RefreshEntries();
            StatusText.Text = "Archive created from folder";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to add" };
        if (dialog.ShowDialog() != true) return;
        try { await RunBusyAsync("Adding files…", () => service.AddFiles(archivePath!, dialog.FileNames)); RefreshEntries(); StatusText.Text = "Files added"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var selected = SelectedRows().Select(x => x.Name).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select one or more entries first."); return; }
        var dialog = new OpenFolderDialog { Title = "Choose extraction folder" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var progress = new Progress<ArchiveProgress>(p =>
            {
                OperationProgressBar.Value = p.PercentComplete;
                StatusText.Text = $"Extracting {p.CurrentEntry} · {p.PercentComplete:0}%";
            });
            await RunBusyAsync("Extracting selected entries…", token => service.ExtractEntriesAny(archivePath!, selected, dialog.FolderName, currentArchivePassword, progress, token), progress);
            StatusText.Text = "Extraction complete";
        }
        catch (OperationCanceledException) { StatusText.Text = "Extraction cancelled."; }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void ExtractAll_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var dialog = new OpenFolderDialog { Title = "Choose extraction folder" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var names = service.ListAny(archivePath!, currentArchivePassword).Select(x => x.Name).ToList();
            var progress = new Progress<ArchiveProgress>(p =>
            {
                OperationProgressBar.Value = p.PercentComplete;
                StatusText.Text = $"Extracting {p.CurrentEntry} · {p.PercentComplete:0}%";
            });
            await RunBusyAsync("Extracting all entries…", token => service.ExtractEntriesAny(archivePath!, names, dialog.FolderName, currentArchivePassword, progress, token), progress);
            StatusText.Text = $"Extracted {names.Count} entries";
        }
        catch (OperationCanceledException) { StatusText.Text = "Extraction cancelled."; }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var path = archivePath!;
        try
        {
            StatusText.Text = "Verifying archive data and CRC checks…";
            var count = 0;
            var progress = new Progress<ArchiveProgress>(p =>
            {
                OperationProgressBar.Value = p.PercentComplete;
                StatusText.Text = $"Verifying {p.CurrentEntry} · {p.PercentComplete:0}%";
            });
            await RunBusyAsync("Verifying archive data and CRC checks…", token => count = service.VerifyAny(path, currentArchivePassword, progress, token), progress);
            StatusText.Text = $"Verified {count} file entries; no corruption detected";
            MessageBox.Show($"Verified {count} file entries. No corruption was detected.", "Archive verification", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { StatusText.Text = "Verification cancelled."; }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var selected = SelectedRows().Select(x => x.Name).ToList();
        if (selected.Count == 0) return;
        if (MessageBox.Show($"Delete {selected.Count} selected entr(y/ies)?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await RunBusyAsync("Updating archive…", () => service.DeleteEntries(archivePath!, selected)); RefreshEntries(); StatusText.Text = "Entries deleted"; }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireArchive()) return;
        var row = SelectedRows().FirstOrDefault();
        if (row is null) { MessageBox.Show("Select one entry to rename."); return; }
        var dialog = new RenameWindow(row.DisplayName) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { var newName = currentFolderPrefix + dialog.NewName + (row.Kind == "Folder" ? "/" : ""); await RunBusyAsync("Renaming archive entry…", () => service.RenameEntry(archivePath!, row.Name, newName)); RefreshEntries(); StatusText.Text = "Entry renamed"; }
        catch (Exception ex) { ShowError(ex); }
    }


    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRows().FirstOrDefault();
        if (row is null || row.Kind == "Folder" || !RequireArchive()) return;
        var directory = Path.Combine(Path.GetTempPath(), "ArcZipWindows", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var tempFile = Path.Combine(directory, Path.GetFileName(row.Name));
        try
        {
            await RunBusyAsync("Preparing preview…", () => service.ExportEntryToFile(archivePath!, row.Name, tempFile, currentArchivePassword));
            new ArchivePreviewWindow(row.DisplayName, tempFile) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
            ShowError(ex);
        }
    }

    private async void EditExternally_Click(object sender, RoutedEventArgs e)
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
            await RunBusyAsync("Preparing external edit…", () => service.ExportEntryToFile(archivePath!, row.Name, tempFile, currentArchivePassword));
            Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
            pendingEditArchivePath = archivePath;
            pendingEditEntryName = row.Name;
            pendingEditFile = tempFile;
            SaveBackButton.IsEnabled = true;
            StatusText.Text = $"Editing {row.Name} externally. Save in that app, then click Save back to archive.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void SaveBack_Click(object sender, RoutedEventArgs e)
    {
        if (pendingEditArchivePath is null || pendingEditEntryName is null || pendingEditFile is null || !File.Exists(pendingEditFile))
        {
            MessageBox.Show("No pending external edit was found.");
            CleanupPendingEdit();
            return;
        }
        try
        {
            await RunBusyAsync("Saving changes to archive…", () => service.ReplaceEntry(pendingEditArchivePath, pendingEditEntryName, pendingEditFile));
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

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (archivePath is null) return;
        var prompt = new PasswordPromptWindow(Path.GetFileName(archivePath)) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        try
        {
            var password = prompt.Password;
            var entries = service.ListAny(archivePath, password);
            currentArchivePassword = password;
            EntriesGrid.ItemsSource = entries.Where(x => x.Name.Contains(SearchBox.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            UpdateArchiveCapabilities();
            StatusText.Text = "Archive unlocked for reading";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private Task RunBusyAsync(string message, Action action) =>
        RunBusyCoreAsync(message, _ => action(), null, cancellable: false);

    private Task RunBusyAsync(string message, Action<CancellationToken> action, IProgress<ArchiveProgress>? progress = null) =>
        RunBusyCoreAsync(message, action, progress, cancellable: true);

    private async Task RunBusyCoreAsync(string message, Action<CancellationToken> action,
        IProgress<ArchiveProgress>? progress, bool cancellable)
    {
        if (isBusy) return;
        isBusy = true;
        operationCts = new CancellationTokenSource();
        ToolbarActions.IsEnabled = false;
        ApiButton.IsEnabled = false;
        CreateArchiveButton.IsEnabled = false;
        CreateFromFolderButton.IsEnabled = false;
        OpenArchiveButton.IsEnabled = false;
        CompressionLevelBox.IsEnabled = false;
        CancelOperationButton.IsEnabled = cancellable;
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = progress is null;
        OperationProgressBar.Value = 0;
        StatusText.Text = message;
        try
        {
            await Task.Run(() => action(operationCts.Token));
            OperationProgressBar.Value = 100;
        }
        finally
        {
            CancelOperationButton.IsEnabled = false;
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = false;
            operationCts.Dispose();
            operationCts = null;
            isBusy = false;
            ToolbarActions.IsEnabled = true;
            ApiButton.IsEnabled = true;
            CreateArchiveButton.IsEnabled = true;
            CreateFromFolderButton.IsEnabled = true;
            OpenArchiveButton.IsEnabled = true;
            CompressionLevelBox.IsEnabled = true;
            UpdateArchiveCapabilities();
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        operationCts?.Cancel();
        StatusText.Text = "Cancelling operation…";
    }

    private void UpdateArchiveCapabilities()
    {
        var writable = archivePath is not null && ZipArchiveService.CanModifyFormat(archivePath) && string.IsNullOrEmpty(currentArchivePassword) && !nestedReadOnly;
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
