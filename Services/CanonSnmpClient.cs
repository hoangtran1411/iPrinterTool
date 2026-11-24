using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace iPrinterTool.Services;

public sealed class PrinterState
{
    // hrPrinterDetectedErrorState (RFC 2790): bit 0 is the most significant bit of the first byte
    private static readonly string[] Names =
    {
        "lowPaper", "noPaper", "lowToner", "noToner", "doorOpen", "jammed", "offline", "serviceRequested",
        "inputTrayMissing", "outputTrayMissing", "markerSupplyMissing", "outputNearFull", "outputFull",
        "inputTrayEmpty", "overduePreventMaint"
    };

    public byte[] Raw { get; }
    public PrinterState(byte[] raw) => Raw = raw;

    private bool Bit(int index)
    {
        var byteIndex = index / 8;
        return byteIndex < Raw.Length && (Raw[byteIndex] & (0x80 >> (index % 8))) != 0;
    }

    public bool NoPaper => Bit(1);

    public string Describe()
    {
        var active = Names.Where((_, i) => Bit(i)).ToList();
        var raw = Raw.Length == 0 ? "empty" : Convert.ToHexString(Raw);
        return $"{(active.Count == 0 ? "OK" : string.Join(", ", active))} (raw={raw})";
    }
}

public sealed class CanonSnmpClient
{
    private const string ErrorStateOid = "1.3.6.1.2.1.25.3.5.1.2.1"; // hrPrinterDetectedErrorState, device index 1

    private readonly IPEndPoint _endpoint;
    private readonly OctetString _community = new("public");

    public CanonSnmpClient(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            var hosts = Dns.GetHostAddresses(ip);
            address = hosts.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                      ?? hosts.FirstOrDefault()
                      ?? throw new InvalidOperationException($"Cannot resolve host: {ip}");
        }
        _endpoint = new IPEndPoint(address, 161);
    }

    public async Task<PrinterState> GetStateAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var result = await Messenger.GetAsync(
            VersionCode.V1, _endpoint, _community,
            new List<Variable> { new(new ObjectIdentifier(ErrorStateOid)) },
            cts.Token);

        var data = result[0].Data;
        if (data is OctetString os) return new PrinterState(os.GetRaw());

        throw new InvalidOperationException($"SNMP trả về kiểu không mong đợi: {data.TypeCode}");
    }
}