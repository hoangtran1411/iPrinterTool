using System.Globalization;
using System.Text;
using AngleSharp;

namespace iPrinterTool.Services;

public sealed record PrintJob(
    string JobNumber, string Result, DateTime Start, DateTime End,
    string JobType, string FileName, string UserName,
    int OriginalPages, int OutputPages, string SheetsCopies, string ErrorCode);

public sealed class CanonJobLogClient
{
    private const string DateFormat = "dd/MM yyyy hh:mm:ss tt";

    private readonly CanonSession _session;

    public CanonJobLogClient(CanonSession session) => _session = session;

    public async Task<IReadOnlyList<PrintJob>> GetJobLogAsync(CancellationToken ct = default)
    {
        await _session.EnsureLoginAsync(ct);

        using var resp = await _session.Http.GetAsync($"{_session.BaseUrl}/jhis_plist.html", ct);
        if (CanonSession.IsSessionError(resp))
        {
            _session.Invalidate();
            throw new InvalidOperationException("Phiên đăng nhập hết hạn, bấm lại để đăng nhập lại.");
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var html = Encoding.UTF8.GetString(bytes);
        var doc = await BrowsingContext.New(Configuration.Default)
                                       .OpenAsync(r => r.Content(html), ct);

        var jobs = new List<PrintJob>();
        foreach (var tr in doc.QuerySelectorAll(".ItemListComponent tbody tr"))
        {
            var td = tr.QuerySelectorAll("td").Select(x => x.TextContent.Trim()).ToArray();
            if (td.Length != 11) continue;

            jobs.Add(new PrintJob(
                td[0], td[1],
                DateTime.ParseExact(td[2], DateFormat, CultureInfo.InvariantCulture),
                DateTime.ParseExact(td[3], DateFormat, CultureInfo.InvariantCulture),
                td[4], td[5], td[6],
                int.TryParse(td[7], out var op) ? op : 0,
                int.TryParse(td[8], out var outp) ? outp : 0,
                td[9], td[10]));
        }
        return jobs;
    }
}