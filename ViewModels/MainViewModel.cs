using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CanonPaperClient _client;

    public JobLogViewModel JobLog { get; }

    public MainViewModel()
    {
        var session = new CanonSession("10.90.39.20");
        _client = new CanonPaperClient(session);
        JobLog = new JobLogViewModel(new JobLogService(new CanonJobLogClient(session)));
    }

    [ObservableProperty] private string _mpSize = "...";
    [ObservableProperty] private string _drawerSize = "...";
    [ObservableProperty] private string _mpButtonText = "Switch";
    [ObservableProperty] private string _drawerButtonText = "Switch";
    [ObservableProperty] private string _status = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ToggleMpCommand), nameof(ToggleDrawerCommand))]
    private bool _isBusy;

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
        IsBusy = true;
        Status = "";
        try { Apply(await action()); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => RunAsync(() => _client.GetAsync());

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleMpAsync() => RunAsync(() => _client.ToggleAsync(Tray.MultiPurpose));

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ToggleDrawerAsync() => RunAsync(() => _client.ToggleAsync(Tray.Drawer1));
}