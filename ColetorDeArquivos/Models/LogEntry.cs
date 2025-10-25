using System;

namespace ColetorDeArquivos.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public class LogEntry
{
    public LogEntry(string message, LogLevel level)
    {
        Timestamp = DateTime.Now;
        Message = message;
        Level = level;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public LogLevel Level { get; }

    public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] {Message}";
}
