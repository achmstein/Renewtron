using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Asic.KeyTool;

/// <summary>A request the server wants a person to handle, as Renewtron describes it.</summary>
public sealed class ServerRequest
{
    public Guid Id { get; set; }
    public string BusinessName { get; set; } = "";
    public string Abn { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string GivenNames { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The desktop end of the manual queue. Renewtron keeps the list of ASIC key requests it
/// couldn't get through; this shows them to the person who fills in ASIC's form by hand and
/// records what came back. Logs in with the admin's own Renewtron account (a cookie session,
/// the same as the browser).
/// </summary>
public sealed class RenewtronServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly KeyToolSettings _settings;
    private readonly HttpClient _http;
    private bool _loggedIn;

    public RenewtronServer(KeyToolSettings settings)
    {
        _settings = settings;
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        if (Uri.TryCreate(settings.ServerUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            _http.BaseAddress = baseUri;
    }

    public bool IsConfigured =>
        _http.BaseAddress != null &&
        !string.IsNullOrWhiteSpace(_settings.ServerEmail) &&
        !string.IsNullOrWhiteSpace(_settings.ServerPassword);

    public string Host => _http.BaseAddress?.Host ?? "(no server URL)";

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_loggedIn) return;
        if (!IsConfigured) throw new InvalidOperationException("No Renewtron login (menu → Settings → Renewtron server).");

        using var response = await _http.PostAsJsonAsync("api/login?useCookies=true",
            new { email = _settings.ServerEmail.Trim(), password = _settings.ServerPassword }, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Renewtron rejected the email or password (Settings → Renewtron server).");
        response.EnsureSuccessStatusCode();
        _loggedIn = true;
    }

    /// <summary>What needs a person: rows already taken first, then failed, then still pending.</summary>
    public async Task<List<ServerRequest>> ListAsync(int max, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        using var response = await _http.GetAsync($"api/admin/asic-key-requests/manual?max={max}", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _loggedIn = false;
            throw new InvalidOperationException("Renewtron session expired; run again.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ServerRequest>>(Json, ct) ?? [];
    }

    /// <summary>Marks the row as taken by this person, so the server's own run leaves it alone.</summary>
    public async Task ClaimAsync(Guid id, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        using var response = await _http.PostAsJsonAsync($"api/admin/asic-key-requests/{id}/claim", new { by = Environment.UserName }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("The server has already sent this one.");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Records ASIC's reference from the receipt, or (blank) that it wasn't submitted, with a note.</summary>
    public async Task RecordAsync(Guid id, string? referenceNumber, string? note, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        using var response = await _http.PostAsJsonAsync($"api/admin/asic-key-requests/{id}/outcome", new { referenceNumber, note }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Gives a taken row back to the queue.</summary>
    public async Task ReleaseAsync(Guid id, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        using var response = await _http.PostAsync($"api/admin/asic-key-requests/{id}/release", null, ct);
        response.EnsureSuccessStatusCode();
    }
}
