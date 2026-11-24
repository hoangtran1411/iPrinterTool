using System.Collections.ObjectModel;
using System.Windows;

namespace iPrinterTool.Services;

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

public sealed class AppLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public AppLogLevel Level { get; init; }
    public string Message { get; init; } = "";

    public string TimeString => Timestamp.ToString("HH:mm:ss");

    public string LevelString => Level switch
    {
        AppLogLevel.Info => "[INFO]",
        AppLogLevel.Warning => "[WARN]",
        AppLogLevel.Error => "[ERROR]",
        _ => "[LOG]"
    };

    public string LevelColor => Level switch
    {
        AppLogLevel.Info => "#2ECC71",    // Green
        AppLogLevel.Warning => "#F1C40F", // Yellow
        AppLogLevel.Error => "#E74C3C",   // Red
        _ => "#A0AEC0"
    };

    public string FormattedLine => $"[{TimeString}]{LevelString} {Message}";
}

public static class AppLogger
{
    private const int MaxEntries = 1000;
    private static readonly object SyncLock = new();

    public static ObservableCollection<AppLogEntry> Entries { get; } = new();

    public static event Action<AppLogEntry>? LogAdded;

    public static void Info(string message) => Log(AppLogLevel.Info, message);

    public static void Warn(string message) => Log(AppLogLevel.Warning, message);

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex == null ? message : $"{message} (Error: {ex.Message})";
        Log(AppLogLevel.Error, msg);
    }

    public static void Log(AppLogLevel level, string message)
    {
        var entry = new AppLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => AddEntry(entry));
        }
        else
        {
            AddEntry(entry);
        }
    }

    private static void AddEntry(AppLogEntry entry)
    {
        lock (SyncLock)
        {
            if (Entries.Count >= MaxEntries)
            {
                Entries.RemoveAt(0);
            }
            Entries.Add(entry);
        }
        LogAdded?.Invoke(entry);
    }

    public static void Clear()
    {
        lock (SyncLock)
        {
            Entries.Clear();
        }
    }
}
