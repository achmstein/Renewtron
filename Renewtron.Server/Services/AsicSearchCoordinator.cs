using System.Collections.Concurrent;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Renewtron.Services;

/// <summary>
/// Shares live ASIC Connect searches per ABN. The wizard starts one as soon as the customer
/// enters their ABN, so by the time they've typed their details the ASIC answer is usually
/// in hand; the check and the background verification then reuse it instead of opening
/// another ~20s ASIC session.
/// </summary>
public interface IAsicSearchCoordinator
{
    /// <summary>The ASIC result for this ABN — a recent one, the one in flight, or a new search.</summary>
    Task<BusinessNamesResult> SearchAsync(string abn);

    /// <summary>Starts a search without waiting for it.</summary>
    void Prefetch(string abn);

    /// <summary>A finished, definitive result from the last few minutes, if there is one.</summary>
    bool TryGetRecent(string abn, out BusinessNamesResult result);
}

public class AsicSearchCoordinator(
    IServiceScopeFactory scopes,
    IMemoryCache cache,
    ILogger<AsicSearchCoordinator> logger) : IAsicSearchCoordinator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    // Caps concurrent ASIC sessions so a burst of prefetches can't hammer ASIC Connect.
    private readonly SemaphoreSlim _sessions = new(4);
    private readonly ConcurrentDictionary<string, Task<BusinessNamesResult>> _inFlight = new();

    /// <summary>
    /// A failure worth retrying (network, ASIC maintenance) as opposed to ASIC's actual answer
    /// ("not due for renewal", "already in progress", no names). Mirrors AsicRenewalClient's own
    /// retry rule.
    /// </summary>
    public static bool IsTransient(BusinessNamesResult result)
    {
        if (result.Success) return false;
        var message = result.ErrorMessage ?? "";
        return message.StartsWith("Search failed:") || message == "Failed to initialize renewal session";
    }

    public bool TryGetRecent(string abn, out BusinessNamesResult result) =>
        cache.TryGetValue(CacheKey(abn), out result!) && result is not null;

    public void Prefetch(string abn) => _ = SearchAsync(abn);

    public Task<BusinessNamesResult> SearchAsync(string abn)
    {
        abn = Modules.Helpers.NormalizeAbn(abn);
        if (TryGetRecent(abn, out var recent)) return Task.FromResult(recent);
        return _inFlight.GetOrAdd(abn, a => Task.Run(() => RunAsync(a)));
    }

    private async Task<BusinessNamesResult> RunAsync(string abn)
    {
        try
        {
            await _sessions.WaitAsync();
            try
            {
                if (TryGetRecent(abn, out var recent)) return recent;

                using var scope = scopes.CreateScope();
                var asic = scope.ServiceProvider.GetRequiredService<IAsicRenewalClient>();
                var started = DateTime.UtcNow;
                var result = await asic.SearchByAbnAsync(abn);
                logger.LogInformation("ASIC search for {Abn} took {Seconds:0.0}s: {Outcome}",
                    abn, (DateTime.UtcNow - started).TotalSeconds, result.Success ? $"{result.BusinessNames.Count} name(s)" : result.ErrorMessage);

                if (!IsTransient(result))
                    cache.Set(CacheKey(abn), result, CacheFor);
                return result;
            }
            finally
            {
                _sessions.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ASIC search for {Abn} threw", abn);
            return BusinessNamesResult.Failed($"Search failed: {ex.Message}");
        }
        finally
        {
            _inFlight.TryRemove(abn, out _);
        }
    }

    private static string CacheKey(string abn) => $"asic_search_{abn}";
}
