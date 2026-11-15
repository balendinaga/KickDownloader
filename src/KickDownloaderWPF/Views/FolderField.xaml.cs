using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KickDownloaderWPF.Views;

public partial class FolderField : UserControl
{
    public FolderField() => InitializeComponent();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output folder",
            InitialDirectory = Directory.Exists(AppSettings.Current.OutputFolder) ? AppSettings.Current.OutputFolder : null,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AppSettings.Current.OutputFolder = dialog.FolderName;
        }
    }
}
