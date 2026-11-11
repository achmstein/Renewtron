using Hangfire;

namespace Renewtron.Services.AsicPortal;

/// <summary>
/// ASIC allows one ASIC Connect session per account: a second login ends the first. Every job that
/// logs in with our agent login (linking names, registering names, checking registrations) holds
/// this lock for its whole session, whatever Hangfire queue it runs on.
/// </summary>
public static class AsicConnectSession
{
    private const string Resource = "renewtron:asic-connect-login";

    /// <summary>Waits up to <paramref name="wait"/> for the login, then holds it until disposed.</summary>
    public static IDisposable Acquire(TimeSpan? wait = null)
    {
        var connection = JobStorage.Current.GetConnection();
        try
        {
            var handle = connection.AcquireDistributedLock(Resource, wait ?? TimeSpan.FromMinutes(20));
            return new Held(connection, handle);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private sealed class Held(IDisposable connection, IDisposable handle) : IDisposable
    {
        public void Dispose()
        {
            handle.Dispose();
            connection.Dispose();
        }
    }
}
