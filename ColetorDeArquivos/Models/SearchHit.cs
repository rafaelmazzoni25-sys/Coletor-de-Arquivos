using System;
using System.ComponentModel;
using System.IO;

namespace ColetorDeArquivos.Models;

public class SearchHit : INotifyPropertyChanged
{
    private bool _isSelected;

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
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public string FileName { get; }
    public string FullPath { get; }
    public string RootPath { get; }
    public long Size { get; }
    public DateTime LastModified { get; }

    public string SizeDisplay => FormatBytes(Size);
    public string LastModifiedDisplay => LastModified.ToString("dd/MM/yyyy HH:mm");

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
}
