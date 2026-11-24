using System.IO;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iPrinterTool", "printer_ip.txt");

    private string _activeIp = "";
    private CanonSession _session = null!;
    private CanonPaperClient _client = null!;

    public JobLogViewModel JobLog { get; }

    [ObservableProperty] private string _printerIp = "";
    [ObservableProperty] private string _activeIpDisplay = "None";
    [ObservableProperty] private string _mpSize = "...";
    [ObservableProperty] private string _drawerSize = "...";
    [ObservableProperty] private string _mpButtonText = "Switch";
    [ObservableProperty] private string _drawerButtonText = "Switch";
    [ObservableProperty] private string _status = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ToggleMpCommand), nameof(ToggleDrawerCommand), nameof(ConnectCommand))]
    private bool _isBusy;

    public MainViewModel()
    {
        _printerIp = LoadSavedIp();
        var ip = string.IsNullOrWhiteSpace(_printerIp) ? "10.90.39.20" : _printerIp.Trim();
        _printerIp = ip;

        _session = new CanonSession(ip);
        _client = new CanonPaperClient(_session);
        _activeIp = ip;
        ActiveIpDisplay = ip;

        JobLog = new JobLogViewModel(
            new JobLogService(new CanonJobLogClient(_session)),
            new CanonSnmpClient(ip));
    }

    private static string LoadSavedIp()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = File.ReadAllText(SettingsPath).Trim();
                if (!string.IsNullOrEmpty(saved))
                    return saved;
            }
        }
        catch { }
        return "10.90.39.20";
    }

    private static void SaveIp(string ip)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, ip);
        }
        catch { }
    }

    public bool ApplyIp(string rawIp, bool save = true)
    {
        var ip = rawIp?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ip))
        {
            Status = "Please enter a printer IP address.";
            return false;
        }

        if (!IPAddress.TryParse(ip, out _) && Uri.CheckHostName(ip) == UriHostNameType.Unknown)
        {
            Status = $"Invalid printer IP address: '{ip}'";
            return false;
        }

        try
        {
            _session?.Dispose();
            _session = new CanonSession(ip);
            _client = new CanonPaperClient(_session);
            JobLog.Configure(_session, ip);

            _activeIp = ip;
            ActiveIpDisplay = ip;

            if (save)
            {
                SaveIp(ip);
            }
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Error updating printer IP: {ex.Message}";
            return false;
        }
    }

    private bool CanRun() => !IsBusy;

    private void Apply(PaperState s)
    {
        MpSize = CanonPaperClient.ToDisplay(s.MpSize);
        DrawerSize = CanonPaperClient.ToDisplay(s.DrawerSize);
        MpButtonText = s.MpSize == CanonPaperClient.A4 ? "Switch to Free Size" : "Switch to A4";
        DrawerButtonText = s.DrawerSize == CanonPaperClient.A4 ? "Switch to Free Size" : "Switch to A4";
    }

    private async Task RunAsync(Func<Task<PaperState>> action)
    {
        var currentInput = PrinterIp?.Trim() ?? "";
        if (!string.IsNullOrEmpty(currentInput) && currentInput != _activeIp)
        {
            if (!ApplyIp(currentInput, save: true))
                return;
        }

        IsBusy = true;
        Status = "";
        try { Apply(await action()); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ConnectAsync()
    {
        if (ApplyIp(PrinterIp, save: true))
        {
            await RefreshAsync();
            await JobLog.RefreshAllAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => RunAsync(() => _client.GetAsync());

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleMpAsync() => RunAsync(() => _client.ToggleAsync(Tray.MultiPurpose));

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleDrawerAsync() => RunAsync(() => _client.ToggleAsync(Tray.Drawer1));
}
