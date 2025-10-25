using System.Windows;
using ColetorDeArquivos.Services;
using ColetorDeArquivos.ViewModels;

namespace ColetorDeArquivos;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(new DialogService(), new FileCollector(), new FileCopyService());
    }
}
