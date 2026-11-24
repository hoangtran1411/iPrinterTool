using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Data;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class JobLogViewModel : ObservableObject
{
    private JobLogService _service;
    private CanonSnmpClient _snmp;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _syncing;
    private bool _checking;
    private bool? _wasOutOfPaper;

    private readonly List<JobRecord> _allJobs = new();
    private readonly List<UserStat> _allStats = new();
    private readonly List<PaperEvent> _allPaperEvents = new();

    public ObservableCollection<JobRecord> Jobs { get; } = new();
    public ObservableCollection<UserStat> Stats { get; } = new();
    public ObservableCollection<PaperEvent> PaperEvents { get; } = new();

    [ObservableProperty] private int _days = 7;
    [ObservableProperty] private string _lastPrint = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _printerState = "Checking...";

    [ObservableProperty] private string _statsSearchText = "";
    [ObservableProperty] private string _paperEventsSearchText = "";
    [ObservableProperty] private string _jobsSearchText = "";

    [ObservableProperty] private string _statsSummary = "0 users";
    [ObservableProperty] private string _paperEventsSummary = "0 events";
    [ObservableProperty] private string _jobsSummary = "0 jobs";

    public JobLogViewModel(JobLogService service, CanonSnmpClient snmp)
    {
        _service = service;
        _snmp = snmp;
        _timer.Tick += async (_, _) => await SyncAndLoadAsync();
        _stateTimer.Tick += async (_, _) => await CheckPrinterAsync();
    }

    public void Configure(CanonSession session, string ip)
    {
        _service = new JobLogService(new CanonJobLogClient(session));
        _snmp = new CanonSnmpClient(ip);
        PrinterState = "Checking...";
        Status = "";
        _wasOutOfPaper = null;
    }

    public async Task RefreshAllAsync()
    {
        await SyncAndLoadAsync();
        await CheckPrinterAsync();
    }

    public void Start()
    {
        _ = SyncAndLoadAsync();
        _timer.Start();
        _ = CheckPrinterAsync();
        _stateTimer.Start();
    }

    private async Task CheckPrinterAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var state = await _snmp.GetStateAsync();
            var desc = state.Describe();
            PrinterState = $"{desc} - checked {DateTime.Now:HH:mm:ss}";

            var outNow = state.NoPaper;
            if (outNow && _wasOutOfPaper != true)
            {
                AppLogger.Error($"Printer is OUT OF PAPER! ({desc})");
                await SyncAndLoadAsync();            // Fetch latest jobs before capturing snapshot
                await _service.RecordPaperOutAsync();
                await LoadAsync();
                AppLogger.Warn("Recorded paper-out snapshot event to database.");
            }
            else if (outNow)
            {
                // Still out of paper
            }
            else if (desc.StartsWith("OK"))
            {
                // OK status
            }
            else
            {
                AppLogger.Warn($"Printer status alert: {desc}");
            }
            _wasOutOfPaper = outNow;
        }
        catch (Exception ex)
        {
            PrinterState = $"SNMP failed: {ex.Message}";
            AppLogger.Error("Printer SNMP status check failed", ex);
        }
        finally { _checking = false; }
    }

    private async Task SyncAndLoadAsync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            AppLogger.Info("Synchronizing print jobs from printer Remote UI...");
            var added = await _service.SyncAsync();
            Status = $"Synced {DateTime.Now:HH:mm:ss}: {added} new job(s)";
            if (added > 0)
            {
                AppLogger.Info($"Sync complete: Recorded {added} new print job(s) in local database.");
            }
            else
            {
                AppLogger.Info("Sync complete: No new print jobs found.");
            }
        }
        catch (Exception ex)
        {
            Status = $"Sync failed: {ex.Message}";
            AppLogger.Error("Print job synchronization failed", ex);
        }
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
            var events = await _service.GetPaperEventsAsync();

            _allJobs.Clear();
            _allJobs.AddRange(jobs);

            _allStats.Clear();
            _allStats.AddRange(stats);

            _allPaperEvents.Clear();
            _allPaperEvents.AddRange(events);

            ApplyStatsFilter();
            ApplyPaperEventsFilter();
            ApplyJobsFilter();

            LastPrint = last is null
                ? "No print job yet"
                : $"{last.UserName} - {last.FileName} ({last.OutputPages} pages) at {last.Start:dd/MM HH:mm}";
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
            AppLogger.Error("Failed to load records from local database", ex);
        }
    }

    partial void OnStatsSearchTextChanged(string value) => ApplyStatsFilter();
    partial void OnPaperEventsSearchTextChanged(string value) => ApplyPaperEventsFilter();
    partial void OnJobsSearchTextChanged(string value) => ApplyJobsFilter();

    [RelayCommand]
    private void ClearStatsSearch() => StatsSearchText = "";

    [RelayCommand]
    private void ClearPaperEventsSearch() => PaperEventsSearchText = "";

    [RelayCommand]
    private void ClearJobsSearch() => JobsSearchText = "";

    public void ApplyStatsFilter()
    {
        RunOnDispatcher(() =>
        {
            var query = StatsSearchText?.Trim() ?? "";
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allStats
                : _allStats.Where(s =>
                    (!string.IsNullOrEmpty(s.UserName) && s.UserName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    s.Jobs.ToString().Contains(query) ||
                    s.Pages.ToString().Contains(query)).ToList();

            Stats.Clear();
            foreach (var s in filtered) Stats.Add(s);

            StatsSummary = string.IsNullOrWhiteSpace(query)
                ? $"{_allStats.Count} user(s)"
                : $"{Stats.Count}/{_allStats.Count} found";
        });
    }

    public void ApplyPaperEventsFilter()
    {
        RunOnDispatcher(() =>
        {
            var query = PaperEventsSearchText?.Trim() ?? "";
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allPaperEvents
                : _allPaperEvents.Where(e =>
                    (!string.IsNullOrEmpty(e.LastUser) && e.LastUser.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.LastFile) && e.LastFile.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.TopUser3h) && e.TopUser3h.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    e.LastPages.ToString().Contains(query) ||
                    e.TopPages3h.ToString().Contains(query) ||
                    e.Time.ToString("dd/MM HH:mm").Contains(query)).ToList();

            PaperEvents.Clear();
            foreach (var e in filtered) PaperEvents.Add(e);

            PaperEventsSummary = string.IsNullOrWhiteSpace(query)
                ? $"{_allPaperEvents.Count} event(s)"
                : $"{PaperEvents.Count}/{_allPaperEvents.Count} found";
        });
    }

    public void ApplyJobsFilter()
    {
        RunOnDispatcher(() =>
        {
            var query = JobsSearchText?.Trim() ?? "";
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allJobs
                : _allJobs.Where(j =>
                    (!string.IsNullOrEmpty(j.JobNumber) && j.JobNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(j.FileName) && j.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(j.UserName) && j.UserName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(j.Result) && j.Result.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(j.JobType) && j.JobType.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(j.ErrorCode) && j.ErrorCode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    j.OutputPages.ToString().Contains(query) ||
                    j.Start.ToString("dd/MM HH:mm").Contains(query)).ToList();

            Jobs.Clear();
            foreach (var j in filtered) Jobs.Add(j);

            JobsSummary = string.IsNullOrWhiteSpace(query)
                ? $"{_allJobs.Count} job(s)"
                : $"{Jobs.Count}/{_allJobs.Count} found";
        });
    }

    private static void RunOnDispatcher(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    partial void OnDaysChanged(int value)
    {
        if (value >= 1) _ = LoadAsync();
    }

    [RelayCommand]
    private Task SyncAsync() => SyncAndLoadAsync();
}