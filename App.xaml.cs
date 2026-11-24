using System.Net;
using System.Windows;
using iPrinterTool.Services;
using iPrinterTool.ViewModels;

namespace iPrinterTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--test-scanner"))
        {
            try
            {
                Console.WriteLine("Running Scanner Self-Tests...");

                var scanner = new PrinterScannerService();

                // 1. Test Subnets
                var subnets = scanner.GetAvailableSubnets("10.90.39.20");
                Console.WriteLine($"Discovered {subnets.Count} subnets:");
                foreach (var s in subnets)
                {
                    Console.WriteLine($"  - {s}");
                }
                if (subnets.Count == 0 || !subnets.Any(s => s.Contains("10.90.39.0/24")))
                {
                    throw new Exception("Subnet discovery test failed!");
                }

                // 2. Test IP Parsing
                var cidrIps = scanner.ParseIpRange("10.14.12.0/24 (Ethernet)");
                if (cidrIps.Count != 254 || !cidrIps[0].Equals(IPAddress.Parse("10.14.12.1")) || !cidrIps[^1].Equals(IPAddress.Parse("10.14.12.254")))
                {
                    throw new Exception($"CIDR parsing failed: count={cidrIps.Count}, first={cidrIps.FirstOrDefault()}, last={cidrIps.LastOrDefault()}");
                }

                var wildcardIps = scanner.ParseIpRange("192.168.1.*");
                if (wildcardIps.Count != 254 || !wildcardIps[0].Equals(IPAddress.Parse("192.168.1.1")))
                {
                    throw new Exception($"Wildcard parsing failed: count={wildcardIps.Count}");
                }

                var rangeIps = scanner.ParseIpRange("10.90.39.10-25");
                if (rangeIps.Count != 16 || !rangeIps[0].Equals(IPAddress.Parse("10.90.39.10")) || !rangeIps[^1].Equals(IPAddress.Parse("10.90.39.25")))
                {
                    throw new Exception($"Range parsing failed: count={rangeIps.Count}");
                }

                var singleIp = scanner.ParseIpRange("10.90.39.20");
                if (singleIp.Count != 1 || !singleIp[0].Equals(IPAddress.Parse("10.90.39.20")))
                {
                    throw new Exception($"Single IP parsing failed: count={singleIp.Count}");
                }

                // 3. Test ViewModel wiring
                var vm = new PrinterScannerViewModel();
                if (vm.SubnetList.Count == 0)
                {
                    throw new Exception("PrinterScannerViewModel SubnetList is empty!");
                }

                var selectedFired = false;
                vm.PrinterSelected += ip => { selectedFired = (ip == "10.90.39.20"); };
                var testItem = new DiscoveredPrinterItemViewModel(new DiscoveredPrinter
                {
                    IpAddress = "10.90.39.20",
                    Name = "Canon iR-ADV C3525",
                    Model = "iR-ADV C3525",
                    Vendor = "Canon",
                    IsCanon = true,
                    PortsDescription = "9100, 80",
                    ResponseTimeMs = 20
                });
                vm.DiscoveredPrinters.Add(testItem);
                vm.UpdateActiveIp("10.90.39.20");
                if (!testItem.IsActive)
                {
                    throw new Exception("UpdateActiveIp did not mark test item as Active!");
                }

                vm.SelectPrinterCommand.Execute(testItem);
                if (!selectedFired)
                {
                    throw new Exception("SelectPrinterCommand did not fire PrinterSelected event!");
                }

                // 4. Test Probe host scan
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var scanResults = await scanner.ScanAsync(new[] { IPAddress.Loopback }, null, cts.Token);
                Console.WriteLine($"Loopback scan completed without error. Found {scanResults.Count} printer(s).");

                Console.WriteLine("ALL SCANNER SELF-TESTS PASSED!");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Self-test error: {ex}");
                Environment.ExitCode = 1;
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        base.OnStartup(e);
    }
}
