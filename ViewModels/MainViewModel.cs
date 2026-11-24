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
    public PrinterScannerViewModel Scanner { get; }
    public AppLogViewModel Log { get; } = new();

    [ObservableProperty] private bool _isSidebarExpanded = true;

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

        Scanner = new PrinterScannerViewModel();
        Scanner.RefreshSubnets(ip);
        Scanner.UpdateActiveIp(ip);
        Scanner.PrinterSelected += selectedIp =>
        {
            AppLogger.Info($"Printer selected from scanner: {selectedIp}");
            PrinterIp = selectedIp;
            _ = ConnectAsync();
        };

        AppLogger.Info($"iPrinterTool initialized. Target printer IP: {ip}");
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
            AppLogger.Warn("Cannot connect: Printer IP address is empty.");
            return false;
        }

        if (!IPAddress.TryParse(ip, out _) && Uri.CheckHostName(ip) == UriHostNameType.Unknown)
        {
            Status = $"Invalid printer IP address: '{ip}'";
            AppLogger.Error($"Invalid printer IP address: '{ip}'");
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
            Scanner.UpdateActiveIp(ip);
            Scanner.RefreshSubnets(ip);

            if (save)
            {
                SaveIp(ip);
            }
            AppLogger.Info($"Configured active printer target to {ip}");
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Error updating printer IP: {ex.Message}";
            AppLogger.Error($"Error updating printer IP to {ip}", ex);
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
        try
        {
            var result = await action();
            Apply(result);
            AppLogger.Info($"Paper state updated on {_activeIp}: MP={MpSize}, Drawer1={DrawerSize}");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AppLogger.Error($"Paper operation failed on {_activeIp}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ConnectAsync()
    {
        AppLogger.Info($"Initiating connection to printer {PrinterIp}...");
        if (ApplyIp(PrinterIp, save: true))
        {
            await RefreshAsync();
            await JobLog.RefreshAllAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync()
    {
        AppLogger.Info($"Refreshing paper tray sizes from {_activeIp}...");
        return RunAsync(() => _client.GetAsync());
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleMpAsync()
    {
        AppLogger.Info($"Toggling Multi-Purpose tray on {_activeIp}...");
        return RunAsync(() => _client.ToggleAsync(Tray.MultiPurpose));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleDrawerAsync()
    {
        AppLogger.Info($"Toggling Drawer 1 on {_activeIp}...");
        return RunAsync(() => _client.ToggleAsync(Tray.Drawer1));
    }
}
