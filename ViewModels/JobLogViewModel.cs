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

    public ObservableCollection<JobRecord> Jobs { get; } = new();
    public ObservableCollection<UserStat> Stats { get; } = new();
    public ObservableCollection<PaperEvent> PaperEvents { get; } = new();

    [ObservableProperty] private int _days = 7;
    [ObservableProperty] private string _lastPrint = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _printerState = "Checking...";

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
            PrinterState = $"{state.Describe()} - checked {DateTime.Now:HH:mm:ss}";

            var outNow = state.NoPaper;
            if (outNow && _wasOutOfPaper != true)
            {
                await SyncAndLoadAsync();            // lấy job mới nhất trước khi chụp
                await _service.RecordPaperOutAsync();
                await LoadAsync();
            }
            _wasOutOfPaper = outNow;
        }
        catch (Exception ex) { PrinterState = $"SNMP failed: {ex.Message}"; }
        finally { _checking = false; }
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
            var events = await _service.GetPaperEventsAsync();

            Jobs.Clear();
            foreach (var j in jobs) Jobs.Add(j);

            Stats.Clear();
            foreach (var s in stats) Stats.Add(s);

            PaperEvents.Clear();
            foreach (var e in events) PaperEvents.Add(e);

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