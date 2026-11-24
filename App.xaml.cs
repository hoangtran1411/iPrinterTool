using System.Net;
using System.Windows;
using iPrinterTool.Data;
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
        base.OnStartup(e);

        if (e.Args.Contains("--test-scanner"))
        {
            try
            {
                Console.WriteLine("Running Scanner Self-Tests...");
                await DatabaseInitializer.InitializeAsync();

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

                // 4. Test AppLogger & AppLogViewModel
                var logVm = new AppLogViewModel();
                AppLogger.Clear();
                AppLogger.Info("Test info message");
                AppLogger.Warn("Test warning message");
                AppLogger.Error("Test error message");

                if (AppLogger.Entries.Count != 3)
                {
                    throw new Exception($"AppLogger failed to add entries. Count: {AppLogger.Entries.Count}");
                }

                var infoEntry = AppLogger.Entries[0];
                var warnEntry = AppLogger.Entries[1];
                var errorEntry = AppLogger.Entries[2];

                if (infoEntry.LevelString != "[INFO]" || infoEntry.LevelColor != "#2ECC71")
                    throw new Exception("AppLogger Info format/color mismatch!");
                if (warnEntry.LevelString != "[WARN]" || warnEntry.LevelColor != "#F1C40F")
                    throw new Exception("AppLogger Warn format/color mismatch!");
                if (errorEntry.LevelString != "[ERROR]" || errorEntry.LevelColor != "#E74C3C")
                    throw new Exception("AppLogger Error format/color mismatch!");

                Console.WriteLine($"Sample log output: {infoEntry.FormattedLine}");
                Console.WriteLine($"Sample log output: {warnEntry.FormattedLine}");
                Console.WriteLine($"Sample log output: {errorEntry.FormattedLine}");

                // 5. Test Probe host scan
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var scanResults = await scanner.ScanAsync(new[] { IPAddress.Loopback }, null, cts.Token);
                Console.WriteLine($"Loopback scan completed without error. Found {scanResults.Count} printer(s).");

                // 6. Test JobLogViewModel Grid Searches
                var jobLogVm = new JobLogViewModel(
                    new JobLogService(new CanonJobLogClient(new CanonSession("10.90.39.20"))),
                    new CanonSnmpClient("10.90.39.20"));

                // Test Stats Search
                typeof(JobLogViewModel).GetField("_allStats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(jobLogVm, new List<UserStat>
                    {
                        new("Alice", 5, 20),
                        new("Bob", 2, 8),
                        new("Charlie", 10, 50)
                    });
                jobLogVm.ApplyStatsFilter();
                if (jobLogVm.Stats.Count != 3) throw new Exception("Stats initial filter failed!");

                jobLogVm.StatsSearchText = "Alice";
                if (jobLogVm.Stats.Count != 1 || jobLogVm.Stats[0].UserName != "Alice")
                    throw new Exception("Stats search 'Alice' failed!");

                jobLogVm.ClearStatsSearchCommand.Execute(null);
                if (jobLogVm.Stats.Count != 3) throw new Exception("ClearStatsSearch failed!");

                // Test PaperEvents Search
                typeof(JobLogViewModel).GetField("_allPaperEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(jobLogVm, new List<PaperEvent>
                    {
                        new() { LastUser = "Alice", LastFile = "Contract.pdf", Time = DateTime.Now },
                        new() { LastUser = "Bob", LastFile = "Photo.jpg", Time = DateTime.Now }
                    });
                jobLogVm.ApplyPaperEventsFilter();
                if (jobLogVm.PaperEvents.Count != 2) throw new Exception("PaperEvents initial filter failed!");

                jobLogVm.PaperEventsSearchText = "Contract";
                if (jobLogVm.PaperEvents.Count != 1 || jobLogVm.PaperEvents[0].LastUser != "Alice")
                    throw new Exception("PaperEvents search 'Contract' failed!");

                jobLogVm.ClearPaperEventsSearchCommand.Execute(null);
                if (jobLogVm.PaperEvents.Count != 2) throw new Exception("ClearPaperEventsSearch failed!");

                // Test Jobs Search
                typeof(JobLogViewModel).GetField("_allJobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(jobLogVm, new List<JobRecord>
                    {
                        new() { JobNumber = "001", FileName = "Report.pdf", UserName = "Alice", Result = "OK" },
                        new() { JobNumber = "002", FileName = "Invoice.pdf", UserName = "Bob", Result = "NG" }
                    });
                jobLogVm.ApplyJobsFilter();
                if (jobLogVm.Jobs.Count != 2) throw new Exception("Jobs initial filter failed!");

                jobLogVm.JobsSearchText = "Invoice";
                if (jobLogVm.Jobs.Count != 1 || jobLogVm.Jobs[0].FileName != "Invoice.pdf")
                    throw new Exception("Jobs search 'Invoice' failed!");

                jobLogVm.ClearJobsSearchCommand.Execute(null);
                if (jobLogVm.Jobs.Count != 2) throw new Exception("ClearJobsSearch failed!");

                Console.WriteLine("ALL SELF-TESTS PASSED (SCANNER, LOGGER & GRID SEARCHES)!");
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

        try
        {
            await DatabaseInitializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fatal database initialization error", ex);
            MessageBox.Show(
                $"Failed to initialize database:\n{ex.Message}\n\nThe application will shut down.",
                "Database Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
