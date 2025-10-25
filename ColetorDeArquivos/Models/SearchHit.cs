using System;
using System.ComponentModel;
using System.IO;
using ColetorDeArquivos.Utilities;

namespace ColetorDeArquivos.Models;

public class SearchHit : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isDuplicate;

    public SearchHit(string fullPath, string rootPath, long size, DateTime lastModified)
    {
        FullPath = fullPath;
        RootPath = rootPath;
        Size = size;
        LastModified = lastModified;
        FileName = Path.GetFileName(fullPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetFlag(ref _isSelected, value, nameof(IsSelected));
    }

    public bool IsDuplicate
    {
        get => _isDuplicate;
        internal set => SetFlag(ref _isDuplicate, value, nameof(IsDuplicate));
    }

    public string FileName { get; }
    public string FullPath { get; }
    public string RootPath { get; }
    public long Size { get; }
    public DateTime LastModified { get; }

    public string SizeDisplay => SizeFormatter.FormatBytes(Size);
    public string LastModifiedDisplay => LastModified.ToString("dd/MM/yyyy HH:mm");

    private void SetFlag(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
