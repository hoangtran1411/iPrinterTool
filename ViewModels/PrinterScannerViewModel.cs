using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iPrinterTool.Services;

namespace iPrinterTool.ViewModels;

public partial class PrinterScannerViewModel : ObservableObject
{
    private readonly PrinterScannerService _scannerService = new();
    private CancellationTokenSource? _scanCts;
    private string _currentActiveIp = "";

    public ObservableCollection<string> SubnetList { get; } = new();
    public ObservableCollection<DiscoveredPrinterItemViewModel> DiscoveredPrinters { get; } = new();

    [ObservableProperty] private string _selectedSubnet = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusText = "Ready to scan.";
    [ObservableProperty] private string _summaryText = "No printers discovered yet.";
    [ObservableProperty] private DiscoveredPrinterItemViewModel? _selectedPrinter;

    public event Action<string>? PrinterSelected;

    public PrinterScannerViewModel()
    {
        RefreshSubnets();
    }

    public void RefreshSubnets(string? configuredIp = null)
    {
        var current = SelectedSubnet;
        SubnetList.Clear();

        var subnets = _scannerService.GetAvailableSubnets(configuredIp);
        foreach (var s in subnets)
        {
            SubnetList.Add(s);
        }

        if (!string.IsNullOrEmpty(current) && SubnetList.Contains(current))
        {
            SelectedSubnet = current;
        }
        else if (SubnetList.Count > 0)
        {
            SelectedSubnet = SubnetList[0];
        }
    }

    public void UpdateActiveIp(string activeIp)
    {
        _currentActiveIp = activeIp?.Trim() ?? "";
        foreach (var p in DiscoveredPrinters)
        {
            p.IsActive = string.Equals(p.IpAddress, _currentActiveIp, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task StartScanAsync()
    {
        if (IsScanning) return;

        var input = SelectedSubnet;
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusText = "Please select or enter a subnet/range to scan.";
            return;
        }

        var ipList = _scannerService.ParseIpRange(input);
        if (ipList.Count == 0)
        {
            StatusText = $"Could not parse IP range: '{input}'. Format example: 10.90.39.0/24 or 192.168.1.*";
            return;
        }

        IsScanning = true;
        ProgressPercent = 0;
        StatusText = $"Scanning {ipList.Count} address(es)...";
        SummaryText = $"Scanning in progress (0/{ipList.Count})...";
        DiscoveredPrinters.Clear();

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        var progress = new Progress<PrinterScannerService.ScanProgress>(report =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (report.Total > 0)
                {
                    ProgressPercent = (int)Math.Round((double)report.Scanned / report.Total * 100);
                }

                if (report.NewlyFound != null)
                {
                    var isCurrent = string.Equals(report.NewlyFound.IpAddress, _currentActiveIp, StringComparison.OrdinalIgnoreCase);
                    var itemVm = new DiscoveredPrinterItemViewModel(report.NewlyFound, isCurrent);
                    DiscoveredPrinters.Add(itemVm);
                }

                StatusText = $"Scanned {report.Scanned}/{report.Total} IPs... Found {DiscoveredPrinters.Count}";
                SummaryText = $"Found {DiscoveredPrinters.Count} printer(s)";
            });
        });

        try
        {
            var results = await _scannerService.ScanAsync(ipList, progress, _scanCts.Token);
            ProgressPercent = 100;
            StatusText = $"Scan finished. Found {results.Count} printer(s).";
            SummaryText = $"{results.Count} printer(s) found";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            SummaryText = $"Scan stopped. Found {DiscoveredPrinters.Count} printer(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private void SelectPrinter(DiscoveredPrinterItemViewModel? printer)
    {
        if (printer == null) return;
        SelectedPrinter = printer;
        PrinterSelected?.Invoke(printer.IpAddress);
    }
}
