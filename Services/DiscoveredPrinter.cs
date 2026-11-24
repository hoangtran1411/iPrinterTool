namespace iPrinterTool.Services;

public sealed class DiscoveredPrinter
{
    public string IpAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Vendor { get; set; } = "";
    public bool IsCanon { get; set; }
    public string PortsDescription { get; set; } = "";
    public long ResponseTimeMs { get; set; }
    public string Description { get; set; } = "";
}
