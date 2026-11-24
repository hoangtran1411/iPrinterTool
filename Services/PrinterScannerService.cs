using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace iPrinterTool.Services;

public sealed class PrinterScannerService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    private static readonly string[] KnownPrinterVendors =
    {
        "Canon", "HP", "Hewlett-Packard", "Epson", "Brother", "Ricoh",
        "Xerox", "Kyocera", "Lexmark", "Konica", "Sharp", "Toshiba", "Samsung", "Oki"
    };

    private static readonly string[] PrinterKeywords =
    {
        "printer", "remote ui", "imagerunner", "imageclass", "imagepress",
        "laserjet", "pagepro", "deskjet", "ecotank", "officejet", "workcentre",
        "bizhub", "copier", "toner", "paper tray", "drawer"
    };

    public record ScanProgress(int Scanned, int Total, DiscoveredPrinter? NewlyFound);

    /// <summary>
    /// Discovers local subnet options based on active network adapters and current printer configuration.
    /// </summary>
    public List<string> GetAvailableSubnets(string? configuredIp = null)
    {
        var subnets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var uni in ipProps.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var ip = uni.Address;
                    var mask = uni.IPv4Mask;
                    if (mask == null || Equals(mask, IPAddress.Any))
                        continue;

                    var networkBytes = ip.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var netBytes = new byte[4];
                    var prefixLength = 0;

                    for (var i = 0; i < 4; i++)
                    {
                        netBytes[i] = (byte)(networkBytes[i] & maskBytes[i]);
                        var m = maskBytes[i];
                        while (m > 0)
                        {
                            if ((m & 0x80) != 0) prefixLength++;
                            m <<= 1;
                        }
                    }

                    var netIp = new IPAddress(netBytes);
                    var label = $"{netIp}/{prefixLength} ({ni.Name})";
                    subnets.Add(label);
                }
            }
        }
        catch { }

        // Also add the configured printer IP's /24 subnet if specified
        if (!string.IsNullOrWhiteSpace(configuredIp))
        {
            var match = Regex.Match(configuredIp.Trim(), @"^(\d{1,3}\.\d{1,3}\.\d{1,3})\.\d{1,3}$");
            if (match.Success)
            {
                var configuredSubnet = $"{match.Groups[1].Value}.0/24 (Configured Printer)";
                subnets.Add(configuredSubnet);
            }
        }

        return subnets.OrderBy(s => s.Contains("Hyper-V") || s.Contains("Default Switch")).ThenBy(s => s).ToList();
    }

    /// <summary>
    /// Parses an input string (e.g. "10.14.12.0/24 (Ethernet)", "10.90.39.*", "10.90.39.1-254", "10.90.39.20")
    /// into a list of IPv4 host addresses to scan.
    /// </summary>
    public List<IPAddress> ParseIpRange(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new();

        var trimmed = input.Trim();
        // Remove interface description if present, e.g. "10.14.12.0/24 (Ethernet)" -> "10.14.12.0/24"
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex > 0)
        {
            trimmed = trimmed[..parenIndex].Trim();
        }

        var results = new List<IPAddress>();

        // 1. Wildcard: 192.168.1.* or 192.168.1.
        var wildcardMatch = Regex.Match(trimmed, @"^(\d{1,3}\.\d{1,3}\.\d{1,3})\.?\*?$");
        if (wildcardMatch.Success && (trimmed.EndsWith("*") || trimmed.EndsWith(".")))
        {
            var prefix = wildcardMatch.Groups[1].Value;
            for (var i = 1; i <= 254; i++)
            {
                if (IPAddress.TryParse($"{prefix}.{i}", out var ip))
                    results.Add(ip);
            }
            return results;
        }

        // 2. CIDR: 192.168.1.0/24
        var cidrMatch = Regex.Match(trimmed, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})/(\d{1,2})$");
        if (cidrMatch.Success &&
            IPAddress.TryParse(cidrMatch.Groups[1].Value, out var baseIp) &&
            int.TryParse(cidrMatch.Groups[2].Value, out var cidr))
        {
            if (cidr is >= 20 and <= 32)
            {
                var baseInt = IpToUInt(baseIp);
                var mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
                var networkInt = baseInt & mask;
                var broadcastInt = networkInt | ~mask;

                var firstHost = networkInt + 1;
                var lastHost = broadcastInt - 1;

                if (cidr == 32)
                {
                    results.Add(baseIp);
                    return results;
                }

                // Limit maximum hosts to 512 for responsiveness
                var count = Math.Min(lastHost - firstHost + 1, 512u);
                for (uint i = 0; i < count; i++)
                {
                    results.Add(UIntToIp(firstHost + i));
                }
                return results;
            }
        }

        // 3. Full IP range: 192.168.1.10-192.168.1.50
        var fullRangeMatch = Regex.Match(trimmed, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s*-\s*(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})$");
        if (fullRangeMatch.Success &&
            IPAddress.TryParse(fullRangeMatch.Groups[1].Value, out var ipStart) &&
            IPAddress.TryParse(fullRangeMatch.Groups[2].Value, out var ipEnd))
        {
            var uStart = IpToUInt(ipStart);
            var uEnd = IpToUInt(ipEnd);
            var min = Math.Min(uStart, uEnd);
            var max = Math.Max(uStart, uEnd);
            var count = Math.Min(max - min + 1, 512u);
            for (uint i = 0; i < count; i++)
            {
                results.Add(UIntToIp(min + i));
            }
            return results;
        }

        // 4. Short range: 192.168.1.10-50
        var rangeMatch = Regex.Match(trimmed, @"^(\d{1,3}\.\d{1,3}\.\d{1,3})\.(\d{1,3})\s*-\s*(\d{1,3})$");
        if (rangeMatch.Success)
        {
            var prefix = rangeMatch.Groups[1].Value;
            if (int.TryParse(rangeMatch.Groups[2].Value, out var start) &&
                int.TryParse(rangeMatch.Groups[3].Value, out var end))
            {
                var min = Math.Clamp(Math.Min(start, end), 1, 254);
                var max = Math.Clamp(Math.Max(start, end), 1, 254);
                for (var i = min; i <= max; i++)
                {
                    if (IPAddress.TryParse($"{prefix}.{i}", out var ip))
                        results.Add(ip);
                }
                return results;
            }
        }

        // 5. Single IP: 192.168.1.20
        if (IPAddress.TryParse(trimmed, out var singleIp))
        {
            results.Add(singleIp);
            return results;
        }

        // 6. Fallback: Treat as prefix if contains 3 dots
        var parts = trimmed.Split('.');
        if (parts.Length == 3 || (parts.Length == 4 && parts[3] == "0"))
        {
            var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}";
            for (var i = 1; i <= 254; i++)
            {
                if (IPAddress.TryParse($"{prefix}.{i}", out var ip))
                    results.Add(ip);
            }
        }

        return results;
    }

    private static uint IpToUInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8)  |
               bytes[3];
    }

    private static IPAddress UIntToIp(uint value)
    {
        return new IPAddress(new byte[]
        {
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF)
        });
    }

    /// <summary>
    /// Probes a list of IP addresses in parallel to discover network printers.
    /// </summary>
    public async Task<List<DiscoveredPrinter>> ScanAsync(
        IEnumerable<IPAddress> addresses,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var targetList = addresses.Distinct().ToList();
        var discovered = new List<DiscoveredPrinter>();
        var scannedCount = 0;
        var total = targetList.Count;
        var syncLock = new object();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 35,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(targetList, parallelOptions, async (ip, token) =>
        {
            try
            {
                var printer = await ProbeHostForPrinterAsync(ip, token);

                int currentScanned;
                lock (syncLock)
                {
                    scannedCount++;
                    currentScanned = scannedCount;
                    if (printer != null)
                    {
                        discovered.Add(printer);
                    }
                }

                progress?.Report(new ScanProgress(currentScanned, total, printer));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                int currentScanned;
                lock (syncLock)
                {
                    scannedCount++;
                    currentScanned = scannedCount;
                }
                progress?.Report(new ScanProgress(currentScanned, total, null));
            }
        });

        lock (syncLock)
        {
            return discovered.OrderByDescending(p => p.IsCanon).ThenBy(p => p.IpAddress).ToList();
        }
    }

    private async Task<DiscoveredPrinter?> ProbeHostForPrinterAsync(IPAddress ip, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Check common printer ports
        // 9100: RAW / JetDirect (standard printer port)
        // 631: IPP (Internet Printing Protocol)
        // 515: LPD/LPR
        // 80: HTTP (Remote UI / Web admin)
        var task9100 = CheckTcpPortAsync(ip, 9100, 450, ct);
        var task631 = CheckTcpPortAsync(ip, 631, 450, ct);
        var task515 = CheckTcpPortAsync(ip, 515, 450, ct);
        var task80 = CheckTcpPortAsync(ip, 80, 450, ct);

        await Task.WhenAll(task9100, task631, task515, task80);

        var port9100 = task9100.Result;
        var port631 = task631.Result;
        var port515 = task515.Result;
        var port80 = task80.Result;

        var openPorts = new List<int>();
        if (port9100) openPorts.Add(9100);
        if (port631) openPorts.Add(631);
        if (port515) openPorts.Add(515);
        if (port80) openPorts.Add(80);

        var isDefinitelyPrinter = port9100 || port631 || port515;

        // Try SNMP query (sysDescr: 1.3.6.1.2.1.1.1.0, sysName: 1.3.6.1.2.1.1.5.0)
        string? snmpDescr = null;
        string? snmpName = null;

        // If any port responded OR as fallback check
        if (isDefinitelyPrinter || port80)
        {
            var snmpResult = await QuerySnmpAsync(ip, ct);
            snmpDescr = snmpResult.sysDescr;
            snmpName = snmpResult.sysName;
            if (snmpResult.success)
            {
                openPorts.Add(161);
            }
        }

        // If not definitely a printer yet, check if SNMP identified it as one
        if (!isDefinitelyPrinter)
        {
            if (!string.IsNullOrEmpty(snmpDescr) && IsPrinterText(snmpDescr))
            {
                isDefinitelyPrinter = true;
            }
        }

        // Check HTTP Remote UI if port 80 is open
        string? httpTitle = null;
        string? httpBody = null;
        if (port80)
        {
            var webInfo = await ProbeHttpAsync(ip, ct);
            httpTitle = webInfo.Title;
            httpBody = webInfo.Body;

            if (!isDefinitelyPrinter && webInfo.IsPrinter)
            {
                isDefinitelyPrinter = true;
            }
        }

        // If not a printer, ignore
        if (!isDefinitelyPrinter)
        {
            return null;
        }

        sw.Stop();

        // Determine Vendor & Model
        var allText = $"{snmpDescr} {snmpName} {httpTitle} {httpBody}";
        var isCanon = allText.Contains("Canon", StringComparison.OrdinalIgnoreCase) ||
                      allText.Contains("imageRUNNER", StringComparison.OrdinalIgnoreCase) ||
                      allText.Contains("imageCLASS", StringComparison.OrdinalIgnoreCase) ||
                      allText.Contains("imagePRESS", StringComparison.OrdinalIgnoreCase) ||
                      allText.Contains("LBP", StringComparison.OrdinalIgnoreCase);

        var vendor = isCanon ? "Canon" : DetectVendor(allText);
        var model = ExtractModelName(snmpDescr, snmpName, httpTitle, vendor);

        return new DiscoveredPrinter
        {
            IpAddress = ip.ToString(),
            Name = !string.IsNullOrWhiteSpace(model) ? model : (isCanon ? "Canon Network Printer" : $"{vendor} Printer"),
            Model = model,
            Vendor = vendor,
            IsCanon = isCanon,
            PortsDescription = string.Join(", ", openPorts),
            ResponseTimeMs = sw.ElapsedMilliseconds,
            Description = snmpDescr ?? httpTitle ?? "Network Printer"
        };
    }

    private static async Task<bool> CheckTcpPortAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool success, string? sysDescr, string? sysName)> QuerySnmpAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(700));

            var endpoint = new IPEndPoint(ip, 161);
            var community = new OctetString("public");

            var result = await Messenger.GetAsync(
                VersionCode.V1,
                endpoint,
                community,
                new List<Variable>
                {
                    new(new ObjectIdentifier("1.3.6.1.2.1.1.1.0")), // sysDescr
                    new(new ObjectIdentifier("1.3.6.1.2.1.1.5.0"))  // sysName
                },
                cts.Token);

            var descr = result.Count > 0 ? result[0].Data.ToString() : null;
            var name = result.Count > 1 ? result[1].Data.ToString() : null;
            return (true, descr, name);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private static async Task<(bool IsPrinter, string? Title, string? Body)> ProbeHttpAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1000));

            var response = await HttpClient.GetAsync($"http://{ip}/", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, null);
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(content))
            {
                return (false, null, null);
            }

            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(content, cts.Token);
            var title = document.Title?.Trim();

            var serverHeader = response.Headers.Server.ToString();
            var isPrinter = IsPrinterText(title) || IsPrinterText(serverHeader) || IsPrinterText(content);

            return (isPrinter, title, content.Length > 500 ? content[..500] : content);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private static bool IsPrinterText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return PrinterKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
               KnownPrinterVendors.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetectVendor(string text)
    {
        foreach (var vendor in KnownPrinterVendors)
        {
            if (text.Contains(vendor, StringComparison.OrdinalIgnoreCase))
                return vendor;
        }
        return "Generic";
    }

    private static string ExtractModelName(string? sysDescr, string? sysName, string? httpTitle, string vendor)
    {
        if (!string.IsNullOrWhiteSpace(sysDescr))
        {
            // e.g. "Canon iR-ADV C3525 / 3530" or "HP LaserJet Pro M404n"
            var cleaned = CleanModelString(sysDescr);
            if (!string.IsNullOrEmpty(cleaned))
                return cleaned;
        }

        if (!string.IsNullOrWhiteSpace(httpTitle))
        {
            // E.g. "Remote UI: Canon iR-ADV C3525" -> "Canon iR-ADV C3525"
            var t = httpTitle.Replace("Remote UI:", "", StringComparison.OrdinalIgnoreCase)
                             .Replace("Remote UI", "", StringComparison.OrdinalIgnoreCase)
                             .Trim(' ', '-', '|', ':');
            if (!string.IsNullOrWhiteSpace(t))
                return t;
        }

        if (!string.IsNullOrWhiteSpace(sysName))
        {
            return sysName.Trim();
        }

        return $"{vendor} Printer";
    }

    private static string CleanModelString(string raw)
    {
        var firstLine = raw.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine)) return "";

        // Remove redundant SNMP prefixes if present
        if (firstLine.Length > 60)
        {
            firstLine = firstLine[..60].Trim();
        }
        return firstLine;
    }
}
