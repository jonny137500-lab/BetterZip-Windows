using Microsoft.Win32;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;

namespace ArcZipWindows;

public partial class MainWindow : Window
{
 private readonly ZipArchiveService service=new(); private string? archivePath;
 public MainWindow(){InitializeComponent();}
 private void Open_Click(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="ZIP archives (*.zip)|*.zip|All files (*.*)|*.*"};if(d.ShowDialog()!=true)return;try{using var _=ZipFile.OpenRead(d.FileName);archivePath=d.FileName;ArchiveLabel.Text=archivePath;RefreshEntries();StatusText.Text="Archive opened";}catch(Exception ex){Error(ex);}}
 private void RefreshEntries(){if(archivePath is null)return;EntriesGrid.ItemsSource=service.List(archivePath).Where(x=>x.Name.Contains(SearchBox.Text??"",StringComparison.OrdinalIgnoreCase)).ToList();StatusText.Text=$"{((System.Collections.ICollection)EntriesGrid.ItemsSource).Count} entries";}
 private void Search_TextChanged(object sender,TextChangedEventArgs e){if(IsInitialized)RefreshEntries();}
 private void Create_Click(object sender,RoutedEventArgs e){var f=new OpenFileDialog{Multiselect=true,Title="Choose files to add to a new ZIP"};if(f.ShowDialog()!=true)return;var s=new SaveFileDialog{Filter="ZIP archive (*.zip)|*.zip",DefaultExt=".zip",FileName="Archive.zip"};if(s.ShowDialog()!=true)return;try{service.Create(s.FileName,f.FileNames);archivePath=s.FileName;ArchiveLabel.Text=archivePath;RefreshEntries();}catch(Exception ex){Error(ex);}}
 private void Add_Click(object sender,RoutedEventArgs e){if(!RequireArchive())return;var d=new OpenFileDialog{Multiselect=true};if(d.ShowDialog()!=true)return;try{service.AddFiles(archivePath!,d.FileNames);RefreshEntries();}catch(Exception ex){Error(ex);}}
 private void Extract_Click(object sender,RoutedEventArgs e){if(!RequireArchive())return;var selected=Selected().Select(x=>x.Name).ToList();if(selected.Count==0){MessageBox.Show("Select one or more entries first.");return;}var d=new OpenFolderDialog{Title="Choose extraction folder"};if(d.ShowDialog()!=true)return;try{service.ExtractEntries(archivePath!,selected,d.FolderName);StatusText.Text="Extraction complete";}catch(Exception ex){Error(ex);}}
 private void Delete_Click(object sender,RoutedEventArgs e){if(!RequireArchive())return;var selected=Selected().Select(x=>x.Name).ToList();if(selected.Count==0)return;if(MessageBox.Show($"Delete {selected.Count} selected entr(y/ies)?","Confirm delete",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;try{service.DeleteEntries(archivePath!,selected);RefreshEntries();}catch(Exception ex){Error(ex);}}
 private void Rename_Click(object sender,RoutedEventArgs e){if(!RequireArchive())return;var row=Selected().FirstOrDefault();if(row is null){MessageBox.Show("Select one entry to rename.");return;}var d=new RenameWindow(row.Name){Owner=this};if(d.ShowDialog()!=true)return;try{service.RenameEntry(archivePath!,row.Name,d.NewName);RefreshEntries();}catch(Exception ex){Error(ex);}}
 private IEnumerable<ArchiveEntryRow> Selected()=>EntriesGrid.SelectedItems.Cast<ArchiveEntryRow>();
 private bool RequireArchive(){if(archivePath is not null&&File.Exists(archivePath))return true;MessageBox.Show("Open a ZIP archive first.");return false;}
 private static void Error(Exception ex)=>MessageBox.Show(ex.Message,"ArcZip Windows",MessageBoxButton.OK,MessageBoxImage.Error);
}
