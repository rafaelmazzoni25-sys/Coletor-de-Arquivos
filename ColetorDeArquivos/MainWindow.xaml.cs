using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;
using ColetorDeArquivos.Models;
using ColetorDeArquivos.Services;

namespace ColetorDeArquivos;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly StringBuilder _logBuilder = new();
    private readonly FileCollector _collector = new();
    private readonly FileCopyService _copyService = new();

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _copyCts;

    private string _destinationFolder = string.Empty;
    private string _extensionsText = ".rar";
    private bool _overwriteExisting;
    private bool _dryRun;
    private bool _followSymlinks;
    private string _statusText = "Pronto.";
    private string _logText = string.Empty;

    public ObservableCollection<string> Roots { get; } = new();
    public ObservableCollection<SearchHit> Hits { get; } = new();

    public string DestinationFolder
    {
        get => _destinationFolder;
        set
        {
            if (_destinationFolder != value)
            {
                _destinationFolder = value;
                OnPropertyChanged(nameof(DestinationFolder));
            }
        }
    }

    public string ExtensionsText
    {
        get => _extensionsText;
        set
        {
            if (_extensionsText != value)
            {
                _extensionsText = value;
                OnPropertyChanged(nameof(ExtensionsText));
                OnPropertyChanged(nameof(CanStartSearch));
            }
        }
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set
        {
            if (_overwriteExisting != value)
            {
                _overwriteExisting = value;
                OnPropertyChanged(nameof(OverwriteExisting));
            }
        }
    }

    public bool DryRun
    {
        get => _dryRun;
        set
        {
            if (_dryRun != value)
            {
                _dryRun = value;
                OnPropertyChanged(nameof(DryRun));
            }
        }
    }

    public bool FollowSymlinks
    {
        get => _followSymlinks;
        set
        {
            if (_followSymlinks != value)
            {
                _followSymlinks = value;
                OnPropertyChanged(nameof(FollowSymlinks));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string LogText
    {
        get => _logText;
        set
        {
            if (_logText != value)
            {
                _logText = value;
                OnPropertyChanged(nameof(LogText));
            }
        }
    }

    public bool IsSearching => _searchCts is not null;
    public bool IsCopying => _copyCts is not null;

    public bool CanEditSearchParameters => !IsSearching && !IsCopying;
    public bool CanStartSearch => CanEditSearchParameters && Roots.Count > 0 && ParseExtensions(ExtensionsText).Count > 0;
    public bool CanCopySelected => !IsSearching && !IsCopying && Hits.Any(h => h.IsSelected);
    public bool CanCopyAll => !IsSearching && !IsCopying && Hits.Any();
    public bool CanSelectRows => Hits.Any();
    public bool CanClearResults => Hits.Any() && !IsSearching && !IsCopying;
    public System.Windows.Visibility BusyVisibility => IsSearching || IsCopying ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Hits.CollectionChanged += HitsOnCollectionChanged;
    }

    private void HitsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<SearchHit>())
            {
                item.PropertyChanged += HitOnPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<SearchHit>())
            {
                item.PropertyChanged -= HitOnPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(CanCopyAll));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(CanSelectRows));
        OnPropertyChanged(nameof(CanClearResults));
        OnPropertyChanged(nameof(CanStartSearch));
    }

    private void HitOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchHit.IsSelected))
        {
            OnPropertyChanged(nameof(CanCopySelected));
        }
    }

    private void ClearHits()
    {
        foreach (var hit in Hits.ToList())
        {
            hit.PropertyChanged -= HitOnPropertyChanged;
        }

        Hits.Clear();
    }

    private void OnAddRoot(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecione a pasta de origem",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            if (!Roots.Any(r => string.Equals(r, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
            {
                Roots.Add(dialog.SelectedPath);
            }
            else
            {
                AppendLog($"[INFO] Pasta já adicionada: {dialog.SelectedPath}");
            }
            OnPropertyChanged(nameof(CanStartSearch));
        }
    }

    private void OnRemoveRoot(object sender, RoutedEventArgs e)
    {
        var selected = RootsListBox.SelectedItems.Cast<string>().ToList();
        foreach (var path in selected)
        {
            Roots.Remove(path);
        }
        OnPropertyChanged(nameof(CanStartSearch));
    }

    private void OnClearRoots(object sender, RoutedEventArgs e)
    {
        Roots.Clear();
        OnPropertyChanged(nameof(CanStartSearch));
    }

    private void OnChooseDestination(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecione a pasta de destino",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            DestinationFolder = dialog.SelectedPath;
        }
    }

    private async void OnStartSearch(object sender, RoutedEventArgs e)
    {
        if (IsSearching || IsCopying)
        {
            return;
        }

        var extensions = ParseExtensions(ExtensionsText);
        if (extensions.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Informe pelo menos uma extensão.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Roots.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Adicione pelo menos uma pasta de origem.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearHits();
        _logBuilder.Clear();
        LogText = string.Empty;
        StatusText = "Buscando arquivos...";
        OnPropertyChanged(nameof(CanCopyAll));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(CanSelectRows));
        OnPropertyChanged(nameof(CanClearResults));

        _searchCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(CanEditSearchParameters));
        OnPropertyChanged(nameof(CanStartSearch));
        OnPropertyChanged(nameof(BusyVisibility));

        try
        {
            var progressHits = new Progress<SearchHit>(hit =>
            {
                Hits.Add(hit);
                StatusText = $"Encontrados: {Hits.Count}";
            });

            var progressLog = new Progress<string>(AppendLog);

            await _collector.CollectAsync(Roots, extensions, FollowSymlinks, progressHits, progressLog, _searchCts.Token);
            StatusText = $"Busca finalizada. Encontrados: {Hits.Count}";
        }
        catch (OperationCanceledException)
        {
            AppendLog("[INFO] Busca cancelada pelo usuário.");
            StatusText = "Busca cancelada.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERRO] Falha durante a busca: {ex.Message}");
            StatusText = "Erro na busca.";
        }
        finally
        {
            _searchCts?.Dispose();
            _searchCts = null;
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(CanEditSearchParameters));
            OnPropertyChanged(nameof(CanStartSearch));
            OnPropertyChanged(nameof(CanCopyAll));
            OnPropertyChanged(nameof(CanSelectRows));
            OnPropertyChanged(nameof(CanClearResults));
            OnPropertyChanged(nameof(BusyVisibility));
        }
    }

    private void OnCancelSearch(object sender, RoutedEventArgs e)
    {
        _searchCts?.Cancel();
    }

    private async void OnCopySelected(object sender, RoutedEventArgs e)
    {
        var selected = Hits.Where(h => h.IsSelected).ToList();
        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Selecione os arquivos desejados.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CopyFilesAsync(selected);
    }

    private async void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (!Hits.Any())
        {
            System.Windows.MessageBox.Show(this, "Nenhum arquivo encontrado.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CopyFilesAsync(Hits.ToList());
    }

    private async Task CopyFilesAsync(IReadOnlyList<SearchHit> hits)
    {
        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            System.Windows.MessageBox.Show(this, "Selecione uma pasta de destino válida.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(DestinationFolder))
        {
            try
            {
                Directory.CreateDirectory(DestinationFolder);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Não foi possível criar a pasta de destino: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        if (IsCopying || IsSearching)
        {
            return;
        }

        _copyCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsCopying));
        OnPropertyChanged(nameof(CanEditSearchParameters));
        OnPropertyChanged(nameof(CanCopyAll));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(BusyVisibility));
        StatusText = "Copiando arquivos...";

        try
        {
            var progressLog = new Progress<string>(AppendLog);
            await _copyService.CopyAsync(hits, DestinationFolder, OverwriteExisting, DryRun, progressLog, _copyCts.Token);
            StatusText = DryRun
                ? $"Simulação concluída. Arquivos simulados: {hits.Count}"
                : $"Cópia finalizada. Arquivos copiados: {hits.Count}";
        }
        catch (OperationCanceledException)
        {
            AppendLog("[INFO] Cópia cancelada pelo usuário.");
            StatusText = "Cópia cancelada.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERRO] Falha durante a cópia: {ex.Message}");
            StatusText = "Erro na cópia.";
        }
        finally
        {
            _copyCts?.Dispose();
            _copyCts = null;
            OnPropertyChanged(nameof(IsCopying));
            OnPropertyChanged(nameof(CanEditSearchParameters));
            OnPropertyChanged(nameof(CanCopyAll));
            OnPropertyChanged(nameof(CanCopySelected));
            OnPropertyChanged(nameof(BusyVisibility));
        }
    }

    private void OnCancelCopy(object sender, RoutedEventArgs e)
    {
        _copyCts?.Cancel();
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var hit in Hits)
        {
            hit.IsSelected = true;
        }
    }

    private void OnInvertSelection(object sender, RoutedEventArgs e)
    {
        foreach (var hit in Hits)
        {
            hit.IsSelected = !hit.IsSelected;
        }
    }

    private void OnClearResults(object sender, RoutedEventArgs e)
    {
        ClearHits();
        StatusText = "Resultados limpos.";
    }

    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void AppendLog(string message)
    {
        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        LogText = _logBuilder.ToString();
    }

    private static List<string> ParseExtensions(string value)
    {
        var parts = value
            .Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.StartsWith('.') ? p.ToLowerInvariant() : $".{p.ToLowerInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts;
    }

    protected virtual void OnPropertyChanged(string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(IsSearching) or nameof(IsCopying))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(CanStartSearch));
                OnPropertyChanged(nameof(CanEditSearchParameters));
                OnPropertyChanged(nameof(BusyVisibility));
            }), DispatcherPriority.DataBind);
        }
    }
}
