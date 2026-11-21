using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Data;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class JobLogViewModel : ObservableObject
{
    private readonly JobLogService _service;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(10) };
    private bool _syncing;

    public ObservableCollection<JobRecord> Jobs { get; } = new();
    public ObservableCollection<UserStat> Stats { get; } = new();

    [ObservableProperty] private int _days = 7;
    [ObservableProperty] private string _lastPrint = "";
    [ObservableProperty] private string _status = "";

    public JobLogViewModel(JobLogService service)
    {
        _service = service;
        _timer.Tick += async (_, _) => await SyncAndLoadAsync();
    }

    public void Start()
    {
        _ = SyncAndLoadAsync();
        _timer.Start();
    }

    private async Task SyncAndLoadAsync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var added = await _service.SyncAsync();
            Status = $"Synced {DateTime.Now:HH:mm:ss}: {added} new job(s)";
        }
        catch (Exception ex) { Status = $"Sync failed: {ex.Message}"; }
        finally { _syncing = false; }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var jobs = await _service.GetRecentAsync();
            var stats = await _service.GetUserStatsAsync(Days);
            var last = await _service.GetLastPrinterJobAsync();

            Jobs.Clear();
            foreach (var j in jobs) Jobs.Add(j);

            Stats.Clear();
            foreach (var s in stats) Stats.Add(s);

            LastPrint = last is null
                ? "No print job yet"
                : $"{last.UserName} - {last.FileName} ({last.OutputPages} pages) at {last.Start:dd/MM HH:mm}";
        }
        catch (Exception ex) { Status = $"Load failed: {ex.Message}"; }
    }

    partial void OnDaysChanged(int value)
    {
        if (value >= 1) _ = LoadAsync();
    }

    [RelayCommand]
    private Task SyncAsync() => SyncAndLoadAsync();
}