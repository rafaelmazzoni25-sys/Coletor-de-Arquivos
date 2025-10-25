using System.Windows.Forms;

namespace ColetorDeArquivos.Services;

public class DialogService : IDialogService
{
    public string? BrowseForFolder(string description, string? initialPath = null)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
