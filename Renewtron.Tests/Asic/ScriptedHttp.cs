using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Renewtron.Tests.AsicClients;

/// <summary>
/// Stands in for ASIC (and the card gateway): answers each request with the next scripted
/// response, in order, and records every request so a test can check exactly what was sent.
/// A request with nothing scripted for it fails the test: the client went somewhere unexpected.
/// </summary>
public sealed class ScriptedHttp : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _replies = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public ScriptedHttp Reply(string body, HttpStatusCode status = HttpStatusCode.OK, string? setCookie = null)
    {
        _replies.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
            if (setCookie is not null) response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
            return response;
        });
        return this;
    }

    public ScriptedHttp Throw(string message)
    {
        _replies.Enqueue(() => throw new HttpRequestException(message));
        return this;
    }

    public int Unused => _replies.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        if (_replies.Count == 0)
            throw new InvalidOperationException($"Unscripted request #{Requests.Count}: {request.Method} {request.RequestUri}");
        return _replies.Dequeue()();
    }

    /// <summary>Every request as text: method, URL, headers (sorted), content type, and the form body one field per line.</summary>
    public string Transcript()
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var (req, body) in Requests)
        {
            sb.Append("#").Append(++n).Append(' ').Append(req.Method).Append(' ').Append(req.RequestUri).Append('\n');
            // HttpHeaders.ToString() is the wire format (e.g. User-Agent tokens joined by spaces).
            foreach (var line in req.Headers.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
                sb.Append("  ").Append(line).Append('\n');
            if (req.Content is not null)
            {
                sb.Append("  Content-Type: ").Append(req.Content.Headers.ContentType).Append('\n');
                var separator = body.TrimStart().StartsWith('{') ? [] : new[] { '&' };
                foreach (var field in separator.Length == 0 ? [body] : body.Split(separator))
                    sb.Append("    ").Append(field).Append('\n');
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Compares text with a checked-in file under Golden/. Set UPDATE_GOLDEN=1 to (re)write the
/// files; review the diff before committing it, since it records exactly what is sent to ASIC.
/// </summary>
public static class Golden
{
    public static void Verify(string name, string actual, [CallerFilePath] string caller = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(caller)!, "Golden", name + ".txt");
        actual = actual.Replace("\r\n", "\n");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"No golden file {path}. Run with UPDATE_GOLDEN=1 to create it, then review it.");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }
}
