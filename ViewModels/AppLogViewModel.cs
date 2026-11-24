using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class AppLogViewModel : ObservableObject
{
    public ObservableCollection<AppLogEntry> Entries => AppLogger.Entries;

    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private int _infoCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _errorCount;

    public AppLogViewModel()
    {
        AppLogger.LogAdded += OnLogAdded;
    }

    private void OnLogAdded(AppLogEntry entry)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => IncrementCount(entry.Level));
        }
        else
        {
            IncrementCount(entry.Level);
        }
    }

    private void IncrementCount(AppLogLevel level)
    {
        switch (level)
        {
            case AppLogLevel.Info:
                InfoCount++;
                break;
            case AppLogLevel.Warning:
                WarningCount++;
                break;
            case AppLogLevel.Error:
                ErrorCount++;
                break;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        AppLogger.Clear();
        InfoCount = 0;
        WarningCount = 0;
        ErrorCount = 0;
    }

    [RelayCommand]
    private void CopyAll()
    {
        try
        {
            var text = string.Join(Environment.NewLine, Entries.Select(e => e.FormattedLine));
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                AppLogger.Info("Application log copied to clipboard.");
            }
        }
        catch { }
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }
}
