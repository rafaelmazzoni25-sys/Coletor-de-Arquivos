namespace ColetorDeArquivos.Services;

public interface IDialogService
{
    string? BrowseForFolder(string description, string? initialPath = null);
}
