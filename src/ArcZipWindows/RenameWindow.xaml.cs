using System.Windows;
namespace ArcZipWindows;
public partial class RenameWindow:Window{public string NewName=>NameBox.Text.Trim();public RenameWindow(string currentName){InitializeComponent();NameBox.Text=currentName;NameBox.SelectAll();NameBox.Focus();}private void Confirm_Click(object sender,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(NewName)){MessageBox.Show("Name cannot be empty.");return;}DialogResult=true;}}
