using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ColetorDeArquivos.Models;
using ColetorDeArquivos.Properties;
using ColetorDeArquivos.Services;

namespace ColetorDeArquivos.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private const int MaxLogEntries = 1000;

    private readonly IDialogService _dialogService;
    private readonly FileCollector _collector;
    private readonly FileCopyService _copyService;
    private readonly Dictionary<FileSignature, List<SearchHit>> _hitsBySignature = new();

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _copyCts;

    private string _destinationFolder = string.Empty;
    private string _extensionsText = ".rar";
    private bool _overwriteExisting;
    private bool _dryRun;
    private bool _followSymlinks;
    private string _statusText = "Pronto.";
    private bool _isSearching;
    private bool _isCopying;
    private bool _isLoadingPreferences;

    public MainWindowViewModel(IDialogService dialogService, FileCollector collector, FileCopyService copyService)
    {
        _dialogService = dialogService;
        _collector = collector;
        _copyService = copyService;

        Roots.CollectionChanged += OnRootsCollectionChanged;
        Hits.CollectionChanged += OnHitsCollectionChanged;

        AddRootCommand = new RelayCommand(_ => AddRoot(), _ => CanEditSearchParameters);
        RemoveRootsCommand = new RelayCommand(RemoveRoots, parameter => CanEditSearchParameters && parameter is IList list && list.Count > 0);
        ClearRootsCommand = new RelayCommand(_ => ClearRoots(), _ => CanEditSearchParameters && Roots.Count > 0);
        ChooseDestinationCommand = new RelayCommand(_ => ChooseDestination(), _ => CanEditSearchParameters);
        StartSearchCommand = new AsyncRelayCommand(_ => StartSearchAsync(), _ => CanStartSearch);
        CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);
        CopySelectedCommand = new AsyncRelayCommand(_ => CopySelectedAsync(), _ => CanCopySelected);
        CopyAllCommand = new AsyncRelayCommand(_ => CopyAllAsync(), _ => CanCopyAll);
        CancelCopyCommand = new RelayCommand(_ => CancelCopy(), _ => IsCopying);
        SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => CanSelectRows && !IsBusy);
        InvertSelectionCommand = new RelayCommand(_ => InvertSelection(), _ => CanSelectRows && !IsBusy);
        CheckDuplicatesCommand = new RelayCommand(_ => CheckDuplicates(), _ => Hits.Any() && !IsBusy);
        ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => CanClearResults);

        LoadPreferences();
        RefreshState();
    }

    public ObservableCollection<string> Roots { get; } = new();

    public ObservableCollection<SearchHit> Hits { get; } = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = new();

    public RelayCommand AddRootCommand { get; }

    public RelayCommand RemoveRootsCommand { get; }

    public RelayCommand ClearRootsCommand { get; }

    public RelayCommand ChooseDestinationCommand { get; }

    public AsyncRelayCommand StartSearchCommand { get; }

    public RelayCommand CancelSearchCommand { get; }

    public AsyncRelayCommand CopySelectedCommand { get; }

    public AsyncRelayCommand CopyAllCommand { get; }

    public RelayCommand CancelCopyCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand InvertSelectionCommand { get; }

    public RelayCommand CheckDuplicatesCommand { get; }

    public RelayCommand ClearResultsCommand { get; }

    public string DestinationFolder
    {
        get => _destinationFolder;
        set
        {
            if (SetProperty(ref _destinationFolder, value))
            {
                SavePreferences();
                RefreshState();
            }
        }
    }

    public string ExtensionsText
    {
        get => _extensionsText;
        set
        {
            if (SetProperty(ref _extensionsText, value))
            {
                SavePreferences();
                RefreshState();
            }
        }
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set
        {
            if (SetProperty(ref _overwriteExisting, value))
            {
                SavePreferences();
            }
        }
    }

    public bool DryRun
    {
        get => _dryRun;
        set
        {
            if (SetProperty(ref _dryRun, value))
            {
                SavePreferences();
            }
        }
    }

    public bool FollowSymlinks
    {
        get => _followSymlinks;
        set
        {
            if (SetProperty(ref _followSymlinks, value))
            {
                SavePreferences();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                RefreshState();
            }
        }
    }

    public bool IsCopying
    {
        get => _isCopying;
        private set
        {
            if (SetProperty(ref _isCopying, value))
            {
                RefreshState();
            }
        }
    }

    public bool IsBusy => IsSearching || IsCopying;

    public bool CanEditSearchParameters => !IsBusy;

    public bool CanCopySelected => !IsBusy && Hits.Any(h => h.IsSelected);

    public bool CanCopyAll => !IsBusy && Hits.Any();

    public bool CanSelectRows => Hits.Any();

    public bool CanClearResults => Hits.Any() && !IsBusy;

    public long TotalHitsSize => Hits.Sum(hit => hit.Size);

    public string TotalHitsSizeDisplay => FormatBytes(TotalHitsSize);

    public string TotalHitsSummary => Hits.Count == 0
        ? "Nenhum arquivo listado."
        : $"Total listado: {TotalHitsSizeDisplay} em {Hits.Count} arquivo(s).";

    public bool CanStartSearch => CanEditSearchParameters && Roots.Count > 0 && ParseExtensions(ExtensionsText).Count > 0;

    private void LoadPreferences()
    {
        try
        {
            _isLoadingPreferences = true;
            var settings = Settings.Default;
            DestinationFolder = settings.DestinationFolder ?? string.Empty;
            ExtensionsText = string.IsNullOrWhiteSpace(settings.ExtensionsText) ? ".rar" : settings.ExtensionsText;
            OverwriteExisting = settings.OverwriteExisting;
            DryRun = settings.DryRun;
            FollowSymlinks = settings.FollowSymlinks;

            if (settings.Roots is { Count: > 0 })
            {
                Roots.Clear();
                foreach (var root in settings.Roots.Cast<string>())
                {
                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                    {
                        Roots.Add(NormalizePath(root));
                    }
                }
            }
        }
        catch
        {
            // Ignorar qualquer falha de carregamento e usar padrões
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private void SavePreferences()
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        try
        {
            var settings = Settings.Default;
            settings.DestinationFolder = DestinationFolder;
            settings.ExtensionsText = ExtensionsText;
            settings.OverwriteExisting = OverwriteExisting;
            settings.DryRun = DryRun;
            settings.FollowSymlinks = FollowSymlinks;

            var collection = settings.Roots ?? new System.Collections.Specialized.StringCollection();
            collection.Clear();
            foreach (var root in Roots)
            {
                collection.Add(root);
            }

            settings.Roots = collection;
            settings.Save();
        }
        catch
        {
            // Ignorar falhas de persistência para não interromper o fluxo da aplicação
        }
    }

    private void AddRoot()
    {
        var folder = _dialogService.BrowseForFolder("Selecione a pasta de origem");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        folder = NormalizePath(folder);
        var existing = Roots.FirstOrDefault(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Roots.Add(folder);
        }
        else if (!string.Equals(existing, folder, StringComparison.Ordinal))
        {
            var index = Roots.IndexOf(existing);
            Roots[index] = folder;
        }

        SavePreferences();
    }

    private void RemoveRoots(object? parameter)
    {
        if (parameter is not IList list || list.Count == 0)
        {
            return;
        }

        var toRemove = list.Cast<object?>().OfType<string>().Select(NormalizePath).ToList();
        foreach (var root in toRemove)
        {
            var existing = Roots.FirstOrDefault(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Roots.Remove(existing);
            }
        }

        SavePreferences();
    }

    private void ClearRoots()
    {
        if (Roots.Count == 0)
        {
            return;
        }

        Roots.Clear();
        SavePreferences();
    }

    private void ChooseDestination()
    {
        var folder = _dialogService.BrowseForFolder("Selecione a pasta de destino", DestinationFolder);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            DestinationFolder = NormalizePath(folder);
        }
    }

    private async Task StartSearchAsync()
    {
        if (!CanStartSearch)
        {
            return;
        }

        SavePreferences();
        CancelSearch();
        CancelCopy();

        _searchCts = new CancellationTokenSource();
        IsSearching = true;
        StatusText = "Buscando arquivos...";
        ClearHits();
        AddLog("Busca iniciada...", LogLevel.Info);

        var extensions = ParseExtensions(ExtensionsText);
        var progress = new Progress<string>(message => AddLog(message));

        try
        {
            await foreach (var hit in _collector.CollectAsync(Roots, extensions, FollowSymlinks, progress, _searchCts.Token))
            {
                Hits.Add(hit);
            }

            StatusText = $"Busca finalizada. Encontrados: {Hits.Count}.";
        }
        catch (OperationCanceledException)
        {
            AddLog("Busca cancelada.", LogLevel.Warning);
            StatusText = "Busca cancelada.";
        }
        catch (Exception ex)
        {
            AddLog($"[ERRO] Falha na busca: {ex.Message}", LogLevel.Error);
            StatusText = "Falha ao executar a busca.";
        }
        finally
        {
            _searchCts?.Dispose();
            _searchCts = null;
            IsSearching = false;
        }
    }

    private void CancelSearch()
    {
        if (_searchCts is null)
        {
            return;
        }

        _searchCts.Cancel();
    }

    private async Task CopySelectedAsync()
    {
        var selection = Hits.Where(h => h.IsSelected).ToList();
        if (selection.Count == 0)
        {
            return;
        }

        await CopyAsync(selection);
    }

    private async Task CopyAllAsync()
    {
        if (Hits.Count == 0)
        {
            return;
        }

        await CopyAsync(Hits.ToList());
    }

    private async Task CopyAsync(IReadOnlyList<SearchHit> hits)
    {
        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            AddLog("[AVISO] Defina a pasta de destino antes de copiar.", LogLevel.Warning);
            return;
        }

        SavePreferences();
        CancelCopy();
        CancelSearch();

        _copyCts = new CancellationTokenSource();
        IsCopying = true;
        StatusText = "Copiando arquivos...";
        AddLog($"Iniciando cópia de {hits.Count} arquivo(s)...", LogLevel.Info);

        var progress = new Progress<string>(message => AddLog(message));

        try
        {
            await _copyService.CopyAsync(hits, DestinationFolder, OverwriteExisting, DryRun, progress, _copyCts.Token);
            StatusText = "Cópia finalizada.";
            AddLog("Cópia concluída.", LogLevel.Info);
        }
        catch (OperationCanceledException)
        {
            AddLog("Cópia cancelada.", LogLevel.Warning);
            StatusText = "Cópia cancelada.";
        }
        catch (Exception ex)
        {
            AddLog($"[ERRO] Falha na cópia: {ex.Message}", LogLevel.Error);
            StatusText = "Falha ao copiar arquivos.";
        }
        finally
        {
            _copyCts?.Dispose();
            _copyCts = null;
            IsCopying = false;
        }
    }

    private void CancelCopy()
    {
        _copyCts?.Cancel();
    }

    private void SelectAll()
    {
        foreach (var hit in Hits)
        {
            hit.IsSelected = true;
        }
    }

    private void InvertSelection()
    {
        foreach (var hit in Hits)
        {
            hit.IsSelected = !hit.IsSelected;
        }
    }

    private void ClearResults()
    {
        ClearHits();
        RefreshState();
    }

    private void ClearHits()
    {
        foreach (var hit in Hits)
        {
            hit.PropertyChanged -= OnHitPropertyChanged;
            hit.IsDuplicate = false;
        }

        Hits.Clear();
        _hitsBySignature.Clear();
        DuplicateGroups.Clear();
        NotifyTotalSizeChanged();
    }

    private void AddLog(string message, LogLevel? levelOverride = null)
    {
        var level = levelOverride ?? InferLevel(message);
        LogEntries.Add(new LogEntry(message, level));
        if (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private static LogLevel InferLevel(string message)
    {
        if (message.StartsWith("[ERRO", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Error;
        }

        if (message.StartsWith("[AVISO", StringComparison.OrdinalIgnoreCase) || message.Contains("permissão", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warning;
        }

        return LogLevel.Info;
    }

    private void OnRootsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshState();
    }

    private void OnHitsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _hitsBySignature.Clear();
            DuplicateGroups.Clear();
            RefreshState();
            NotifyTotalSizeChanged();
            return;
        }

        if (e.NewItems != null)
        {
            foreach (var hit in e.NewItems.OfType<SearchHit>())
            {
                hit.PropertyChanged += OnHitPropertyChanged;
                RegisterHit(hit);
            }
        }

        if (e.OldItems != null)
        {
            foreach (var hit in e.OldItems.OfType<SearchHit>())
            {
                hit.PropertyChanged -= OnHitPropertyChanged;
                UnregisterHit(hit);
            }
        }

        RefreshState();
        NotifyTotalSizeChanged();
    }

    private void OnHitPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchHit.IsSelected))
        {
            RefreshState();
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEditSearchParameters));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(CanCopyAll));
        OnPropertyChanged(nameof(CanSelectRows));
        OnPropertyChanged(nameof(CanClearResults));
        OnPropertyChanged(nameof(CanStartSearch));
        AddRootCommand.RaiseCanExecuteChanged();
        RemoveRootsCommand.RaiseCanExecuteChanged();
        ClearRootsCommand.RaiseCanExecuteChanged();
        ChooseDestinationCommand.RaiseCanExecuteChanged();
        StartSearchCommand.RaiseCanExecuteChanged();
        CancelSearchCommand.RaiseCanExecuteChanged();
        CopySelectedCommand.RaiseCanExecuteChanged();
        CopyAllCommand.RaiseCanExecuteChanged();
        CancelCopyCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        InvertSelectionCommand.RaiseCanExecuteChanged();
        CheckDuplicatesCommand.RaiseCanExecuteChanged();
        ClearResultsCommand.RaiseCanExecuteChanged();
    }

    private void CheckDuplicates()
    {
        if (Hits.Count == 0)
        {
            AddLog("Nenhum arquivo listado para verificar duplicados.");
            return;
        }

        _hitsBySignature.Clear();

        foreach (var hit in Hits)
        {
            hit.IsDuplicate = false;
        }

        foreach (var hit in Hits)
        {
            var signature = FileSignature.FromHit(hit);
            if (!_hitsBySignature.TryGetValue(signature, out var list))
            {
                list = new List<SearchHit>();
                _hitsBySignature[signature] = list;
            }

            list.Add(hit);
        }

        var duplicateGroups = _hitsBySignature.Values.Where(list => list.Count > 1).ToList();

        if (duplicateGroups.Count == 0)
        {
            DuplicateGroups.Clear();
            AddLog("Nenhum arquivo duplicado encontrado entre os resultados.");
            return;
        }

        foreach (var group in duplicateGroups)
        {
            UpdateDuplicateFlags(group);
            var roots = string.Join(", ", group.Select(hit => $"'{hit.RootPath}'"));
            AddLog($"[AVISO] Arquivo duplicado detectado: {group[0].FileName} em {roots}", LogLevel.Warning);
        }

        AddLog($"{duplicateGroups.Count} conjunto(s) de arquivos duplicados identificado(s).", LogLevel.Warning);
        RebuildDuplicateGroups();
    }

    private void RegisterHit(SearchHit hit)
    {
        var key = FileSignature.FromHit(hit);
        if (!_hitsBySignature.TryGetValue(key, out var list))
        {
            list = new List<SearchHit>();
            _hitsBySignature[key] = list;
        }

        list.Add(hit);
        var hasDuplicate = list.Count > 1;
        if (hasDuplicate && list.Count == 2)
        {
            var other = list.FirstOrDefault(existing => !ReferenceEquals(existing, hit)) ?? list[0];
            AddLog($"[AVISO] Arquivo duplicado detectado: {hit.FileName} em '{other.RootPath}' e '{hit.RootPath}'", LogLevel.Warning);
        }

        UpdateDuplicateFlags(list);
        RebuildDuplicateGroups();
    }

    private void UnregisterHit(SearchHit hit)
    {
        var key = FileSignature.FromHit(hit);
        if (!_hitsBySignature.TryGetValue(key, out var list))
        {
            hit.IsDuplicate = false;
            return;
        }

        list.Remove(hit);
        hit.IsDuplicate = false;

        if (list.Count == 0)
        {
            _hitsBySignature.Remove(key);
            RebuildDuplicateGroups();
            return;
        }

        UpdateDuplicateFlags(list);
        RebuildDuplicateGroups();
    }

    private static void UpdateDuplicateFlags(List<SearchHit> hits)
    {
        var isDuplicate = hits.Count > 1;
        foreach (var item in hits)
        {
            item.IsDuplicate = isDuplicate;
        }
    }

    private void RebuildDuplicateGroups()
    {
        var groups = _hitsBySignature
            .Values
            .Where(list => list.Count > 1)
            .Select(list => new DuplicateGroup(list))
            .OrderBy(group => group.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(group => group.Count)
            .ToList();

        DuplicateGroups.Clear();
        foreach (var group in groups)
        {
            DuplicateGroups.Add(group);
        }
    }

    private void NotifyTotalSizeChanged()
    {
        OnPropertyChanged(nameof(TotalHitsSize));
        OnPropertyChanged(nameof(TotalHitsSizeDisplay));
        OnPropertyChanged(nameof(TotalHitsSummary));
    }

    private static string FormatBytes(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double formatted = size;
        int unitIndex = 0;

        while (formatted >= 1024 && unitIndex < units.Length - 1)
        {
            formatted /= 1024;
            unitIndex++;
        }

        return $"{formatted:0.##} {units[unitIndex]}";
    }

    private static List<string> ParseExtensions(string input)
    {
        var parts = input
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.StartsWith('.') ? p : $".{p}")
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .Select(p => p.ToLowerInvariant())
            .Distinct()
            .ToList();

        return parts;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private readonly struct FileSignature : IEquatable<FileSignature>
    {
        private readonly string _fileName;

        private FileSignature(string fileName, long size)
        {
            _fileName = fileName;
            Size = size;
        }

        public long Size { get; }

        public static FileSignature FromHit(SearchHit hit)
        {
            return new FileSignature(hit.FileName.ToLowerInvariant(), hit.Size);
        }

        public bool Equals(FileSignature other)
        {
            return Size == other.Size && string.Equals(_fileName, other._fileName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is FileSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_fileName, Size);
        }
    }
}
