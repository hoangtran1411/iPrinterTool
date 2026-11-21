namespace iPrinterTool.Data;

public class JobRecord
{
    public int Id { get; set; }
    public string JobNumber { get; set; } = "";
    public string Result { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string JobType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string UserName { get; set; } = "";
    public int OriginalPages { get; set; }
    public int OutputPages { get; set; }
    public string SheetsCopies { get; set; } = "";
    public string ErrorCode { get; set; } = "";
}