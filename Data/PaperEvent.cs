namespace iPrinterTool.Data;

public class PaperEvent
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public string LastUser { get; set; } = "";
    public string LastFile { get; set; } = "";
    public int LastPages { get; set; }
    public DateTime LastStart { get; set; }
    public string TopUser3h { get; set; } = "";
    public int TopPages3h { get; set; }
}