using System.Net;
using System.Net.Http;

namespace iPrinterTool.Services;

public sealed class CanonSession
{
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly CookieContainer _cookies = new();
    private bool _loggedIn;

    public HttpClient Http { get; }
    public string BaseUrl { get; }

    public CanonSession(string ip)
    {
        BaseUrl = $"http://{ip}";

        var baseUri = new Uri(BaseUrl);
        _cookies.Add(baseUri, new Cookie("view", "normal"));
        _cookies.Add(baseUri, new Cookie("language", "en"));
        _cookies.Add(baseUri, new Cookie("kind", "standard"));

        Http = new HttpClient(new HttpClientHandler { CookieContainer = _cookies })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public async Task EnsureLoginAsync(CancellationToken ct = default)
    {
        if (_loggedIn) return;

        await _loginLock.WaitAsync(ct);
        try
        {
            if (_loggedIn) return;

            var body = new Dictionary<string, string>
            {
                ["i0017"] = "2",   // General User Mode
                ["i0019"] = ""     // User Name: để trống
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/checkLogin.cgi")
            {
                Content = new FormUrlEncodedContent(body)
            };
            req.Headers.Referrer = new Uri($"{BaseUrl}/");

            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var hasSession = _cookies.GetCookies(new Uri(BaseUrl)).Cast<Cookie>().Any(c => c.Name == "sessid");
            if (!hasSession)
                throw new InvalidOperationException(
                    $"Đăng nhập không nhận được sessid. URL cuối: {resp.RequestMessage?.RequestUri}");

            _loggedIn = true;
        }
        finally { _loginLock.Release(); }
    }

    public void Invalidate() => _loggedIn = false;

    public static bool IsSessionError(HttpResponseMessage resp) =>
        resp.RequestMessage?.RequestUri?.AbsolutePath.Contains("sessError", StringComparison.OrdinalIgnoreCase) == true;
}