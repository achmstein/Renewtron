using Hangfire;

namespace Renewtron.Abstractions;

public interface IClientLinkService
{
    /// <summary>
    /// Attaches every record not yet on a client (sales, leads, key requests,
    /// renewals, messages, key letters) to its client, creating clients and their business
    /// names as needed, and refreshes names from the public register. A dry run does all the
    /// matching and reports what it would do without saving. The result is kept in
    /// <see cref="Services.ClientLinkState"/> for the admin page.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    Task<ClientLinkResult> LinkAsync(bool dryRun, CancellationToken ct = default);

    /// <summary>The recurring run: does nothing until the first real run has been made from the Clients page.</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    Task RunScheduledAsync();

    /// <summary>Moves everything on <paramref name="sourceId"/> onto <paramref name="targetId"/> and deletes the source.</summary>
    Task MergeAsync(Guid sourceId, Guid targetId, CancellationToken ct = default);
}

public sealed class ClientLinkResult
{
    public bool DryRun { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string? Error { get; set; }

    public int ClientsCreated { get; set; }
    public int PossibleDuplicates { get; set; }
    public int EmailsAdded { get; set; }
    public int AbnsAdded { get; set; }
    /// <summary>People added as a second (or later) contact on an ABN someone else already had.</summary>
    public int SharedAbnContacts { get; set; }
    public int NamesAdded { get; set; }
    public int NamesCancelled { get; set; }
    public int AbnsChecked { get; set; }
    public int DueDatesSet { get; set; }
    public int KeysSet { get; set; }

    public int SalesLinked { get; set; }
    public int LeadsLinked { get; set; }
    public int KeyRequestsLinked { get; set; }
    public int RenewalsLinked { get; set; }
    /// <summary>Renewals linked earlier that have since completed, now applied to their name.</summary>
    public int RenewalsCompletedLater { get; set; }
    public int MessagesLinked { get; set; }
    public int KeyLettersLinked { get; set; }
    /// <summary>"No contact" key letters closed off because the key now sits on a client's name.</summary>
    public int KeyLettersResolved { get; set; }
    /// <summary>Records that couldn't be placed (no email, no matching name) and stay unlinked.</summary>
    public int Skipped { get; set; }
}
