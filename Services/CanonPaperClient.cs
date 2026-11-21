using System.Net.Http;
using AngleSharp;

namespace iPrinterTool.Services;

public enum Tray { MultiPurpose, Drawer1 }

public sealed record PaperState(string Token, string MpSize, string MpType, string DrawerSize, string DrawerType);

public sealed class CanonPaperClient
{
    public const string A4 = "2.1";
    public const string FreeSize = "78.1";

    private readonly CanonSession _session;

    public CanonPaperClient(CanonSession session) => _session = session;

    public static string ToDisplay(string code) => code switch
    {
        A4 => "A4",
        FreeSize => "Free Size",
        _ => $"Other ({code})"
    };

    public async Task<PaperState> GetAsync(CancellationToken ct = default)
    {
        await _session.EnsureLoginAsync(ct);

        using var resp = await _session.Http.GetAsync($"{_session.BaseUrl}/p_paper_edit.html", ct);
        if (CanonSession.IsSessionError(resp))
        {
            _session.Invalidate();
            throw new InvalidOperationException("Phiên đăng nhập hết hạn, bấm lại để đăng nhập lại.");
        }

        var html = await resp.Content.ReadAsStringAsync(ct);
        var doc = await BrowsingContext.New(Configuration.Default)
                                       .OpenAsync(r => r.Content(html), ct);

        if (doc.QuerySelector("#i0011B[checked]") is not null)
            throw new NotSupportedException("'Specify When Loading Paper' đang được bật.");

        string Selected(string name)
        {
            var sel = doc.QuerySelector($"select[name='{name}']")
                      ?? throw new InvalidOperationException($"Không thấy select {name}");
            var opt = sel.QuerySelector("option[selected]") ?? sel.QuerySelector("option");
            return opt?.GetAttribute("value") ?? throw new InvalidOperationException($"{name} rỗng");
        }

        var token = doc.QuerySelector("input[name='iToken']")?.GetAttribute("value")
                    ?? throw new InvalidOperationException(
                        $"Không thấy iToken. URL cuối: {resp.RequestMessage?.RequestUri}");

        return new PaperState(token, Selected("i0100"), Selected("i0101"),
                                     Selected("i0102"), Selected("i0103"));
    }

    public async Task<PaperState> ToggleAsync(Tray tray, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);

        var current = tray == Tray.MultiPurpose ? s.MpSize : s.DrawerSize;
        var target = current == A4 ? FreeSize : A4;

        var mp = tray == Tray.MultiPurpose ? target : s.MpSize;
        var drawer = tray == Tray.Drawer1 ? target : s.DrawerSize;

        var body = new Dictionary<string, string>
        {
            ["iToken"] = s.Token,
            ["i0011"] = "1",
            ["i0100"] = mp,
            ["i0101"] = s.MpType,
            ["i0102"] = drawer,
            ["i0103"] = s.DrawerType,
            ["i2300"] = s.MpType,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_session.BaseUrl}/cgi/p_paper_edit.cgi")
        {
            Content = new FormUrlEncodedContent(body)
        };
        req.Headers.Referrer = new Uri($"{_session.BaseUrl}/p_paper_edit.html");

        using var resp = await _session.Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var after = await GetAsync(ct);
        if (after.MpSize != mp || after.DrawerSize != drawer)
            throw new InvalidOperationException("Máy in không nhận thay đổi.");

        return after;
    }
}