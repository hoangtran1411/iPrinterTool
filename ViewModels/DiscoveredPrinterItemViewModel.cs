using CommunityToolkit.Mvvm.ComponentModel;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class DiscoveredPrinterItemViewModel : ObservableObject
{
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _vendor = "";
    [ObservableProperty] private bool _isCanon;
    [ObservableProperty] private string _portsDescription = "";
    [ObservableProperty] private long _responseTimeMs;
    [ObservableProperty] private bool _isActive;

    public string ResponseTimeDisplay => $"{ResponseTimeMs} ms";

    public DiscoveredPrinterItemViewModel(DiscoveredPrinter model, bool isActive = false)
    {
        _ipAddress = model.IpAddress;
        _name = model.Name;
        _model = model.Model;
        _vendor = model.Vendor;
        _isCanon = model.IsCanon;
        _portsDescription = model.PortsDescription;
        _responseTimeMs = model.ResponseTimeMs;
        _isActive = isActive;
    }
}
