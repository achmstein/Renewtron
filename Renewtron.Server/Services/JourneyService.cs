using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>The kinds of record a journey links, as the admin's record pages name them.</summary>
public enum JourneyRecord { Lead, Search, Run, Order, Sale, Email }

/// <summary>One thing that happened, in a sentence, on the record it happened to.</summary>
/// <param name="Meaning">How it reads: action (a person must act), waiting, working, done, quiet (the admin's status colours).</param>
public sealed record JourneyEvent(DateTime At, string Text, string? Detail, string Meaning, JourneyRecord Record, Guid RecordId);

public sealed record Journey(IReadOnlyList<JourneyEvent> Events);

/// <summary>
/// Everything that happened around one record (a lead, a search, an ASIC run, a paid renewal, an
/// Ontraport sale), in time order, as plain sentences. Every record page of the same journey shows
/// the same story, so moving between them doesn't change what you're reading.
///
/// A journey is the records linked to each other: a lead and its search; the names found and the
/// renewals started for them; each renewal's paid order, its other ASIC runs and its Ontraport sale;
/// and the emails sent about any of them. Clients are not followed: a client's other renewals are
/// other journeys.
/// </summary>
public interface IJourneyService
{
    Task<Journey?> ForAsync(JourneyRecord kind, Guid id, CancellationToken ct);
}

public class JourneyService(ApplicationDbContext db) : IJourneyService
{
    /// <summary>A journey this size means a link was followed too far; stop gathering.</summary>
    public const int MaxRecords = 200;

    public async Task<Journey?> ForAsync(JourneyRecord kind, Guid id, CancellationToken ct)
    {
        var set = new Gathered();
        switch (kind)
        {
            case JourneyRecord.Lead: set.Leads.Add(id); break;
            case JourneyRecord.Search: set.Searches.Add(id); break;
            case JourneyRecord.Run: set.Runs.Add(id); break;
            case JourneyRecord.Order: set.Orders.Add(id); break;
            case JourneyRecord.Sale: set.Sales.Add(id); break;
            default: return null;
        }

        await GatherAsync(set, ct);
        var events = await DescribeAsync(set, ct);
        return events.Count == 0 ? null : new Journey(events);
    }

    /* ───────────── Which records belong ───────────── */

    private sealed class Gathered
    {
        public HashSet<Guid> Leads { get; } = [];
        public HashSet<Guid> Searches { get; } = [];
        public HashSet<Guid> Runs { get; } = [];
        public HashSet<Guid> Orders { get; } = [];
        public HashSet<Guid> Sales { get; } = [];
        public int Count => Leads.Count + Searches.Count + Runs.Count + Orders.Count + Sales.Count;
    }

    /// <summary>Follows every link both ways until nothing new turns up.</summary>
    private async Task GatherAsync(Gathered g, CancellationToken ct)
    {
        for (var before = -1; before != g.Count && g.Count < MaxRecords;)
        {
            before = g.Count;
            var leads = g.Leads.ToList();
            var searches = g.Searches.ToList();
            var runs = g.Runs.ToList();
            var orders = g.Orders.ToList();
            var sales = g.Sales.ToList();

            // A lead: its search, and the renewals it started.
            foreach (var s in await db.Leads.AsNoTracking().Where(l => leads.Contains(l.Id) && l.SearchLogId != null).Select(l => l.SearchLogId!.Value).ToListAsync(ct))
                g.Searches.Add(s);
            // A search: the lead it came with, and the renewals of the names it found.
            foreach (var l in await db.Leads.AsNoTracking().Where(l => l.SearchLogId != null && searches.Contains(l.SearchLogId.Value)).Select(l => l.Id).ToListAsync(ct))
                g.Leads.Add(l);
            foreach (var r in await db.RenewalRequests.AsNoTracking()
                         .Where(r => (r.LeadId != null && leads.Contains(r.LeadId.Value)) || searches.Contains(r.SearchResult.SearchLogId))
                         .Select(r => r.Id).ToListAsync(ct))
                g.Runs.Add(r);

            // An ASIC run: its lead, search and paid renewal.
            foreach (var r in await db.RenewalRequests.AsNoTracking().Where(r => runs.Contains(r.Id))
                         .Select(r => new { r.LeadId, r.RenewalOrderId, r.SearchResult.SearchLogId }).ToListAsync(ct))
            {
                if (r.LeadId is { } l) g.Leads.Add(l);
                if (r.RenewalOrderId is { } o) g.Orders.Add(o);
                g.Searches.Add(r.SearchLogId);
            }

            // A paid renewal: its runs and where it was sold.
            foreach (var r in await db.RenewalRequests.AsNoTracking().Where(r => r.RenewalOrderId != null && orders.Contains(r.RenewalOrderId.Value)).Select(r => r.Id).ToListAsync(ct))
                g.Runs.Add(r);
            foreach (var o in await db.RenewalOrders.AsNoTracking().Where(o => orders.Contains(o.Id)).Select(o => new { o.Source, o.SourceId }).ToListAsync(ct))
            {
                if (o.Source == RenewalOrderSource.Ontraport) g.Sales.Add(o.SourceId);
                else g.Runs.Add(o.SourceId); // a website or manual renewal's order is keyed by the run that was paid for
            }

            // An Ontraport sale: its order and the run it started.
            foreach (var s in await db.OntraportSales.AsNoTracking().Where(s => sales.Contains(s.Id) || (s.RenewalRequestId != null && runs.Contains(s.RenewalRequestId.Value)))
                         .Select(s => new { s.Id, s.RenewalRequestId }).ToListAsync(ct))
            {
                g.Sales.Add(s.Id);
                if (s.RenewalRequestId is { } r) g.Runs.Add(r);
            }
            foreach (var o in await db.RenewalOrders.AsNoTracking()
                         .Where(o => (o.Source == RenewalOrderSource.Ontraport && sales.Contains(o.SourceId)) || (o.Source != RenewalOrderSource.Ontraport && runs.Contains(o.SourceId)))
                         .Select(o => o.Id).ToListAsync(ct))
                g.Orders.Add(o);
        }
    }

    /* ───────────── What happened ───────────── */

    private async Task<List<JourneyEvent>> DescribeAsync(Gathered g, CancellationToken ct)
    {
        var events = new List<JourneyEvent>();
        void Add(DateTime? at, string text, string meaning, JourneyRecord record, Guid id, string? detail = null)
        {
            if (at is { } t) events.Add(new(DateTime.SpecifyKind(t, DateTimeKind.Utc), text, string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(), meaning, record, id));
        }

        var searchIds = g.Searches.ToList();
        var searches = await db.SearchLogs.AsNoTracking().Include(s => s.Results).Where(s => searchIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in searches)
        {
            var who = s.InitiatedBy switch { SearchInitiator.Admin => "Staff looked up", SearchInitiator.System => "Renewtron looked up", _ => "The visitor looked up" };
            if (s.Success)
            {
                var names = s.Results.Select(r => r.BusinessName).ToList();
                var found = names.Count == 0 ? "no business names" : $"{Plural(names.Count, "name")}: {Join(names)}";
                Add(s.SearchedAt, $"{who} ABN {s.Abn} at ASIC: {found}", "done", JourneyRecord.Search, s.Id);
            }
            else
                Add(s.SearchedAt, $"{who} ABN {s.Abn}, but ASIC didn't answer", "quiet", JourneyRecord.Search, s.Id, s.ErrorMessage);
            if (s.VerifiedAt is { } v)
                Add(v, "Renewtron checked the lookup with ASIC again", string.IsNullOrEmpty(s.VerificationError) ? "done" : "quiet", JourneyRecord.Search, s.Id, s.VerificationError);
        }

        var leadIds = g.Leads.ToList();
        foreach (var l in await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id)).ToListAsync(ct))
        {
            Add(l.CreatedAt, $"{(string.IsNullOrWhiteSpace(l.FullName) ? "The visitor" : l.FullName)} gave their details for ABN {l.Abn} ({l.Email})", "done", JourneyRecord.Lead, l.Id);
            Add(l.NamesShownAt, $"{FirstName(l.FullName)} saw the names to renew", "done", JourneyRecord.Lead, l.Id);
            Add(l.ReachedPaymentAt, $"{FirstName(l.FullName)} opened the payment page", "done", JourneyRecord.Lead, l.Id);
        }

        var saleIds = g.Sales.ToList();
        foreach (var s in await db.OntraportSales.AsNoTracking().Where(s => saleIds.Contains(s.Id)).ToListAsync(ct))
        {
            Add(s.SyncedAt, $"{s.ContactName} paid ${s.AmountPaid:0.00} in Ontraport to renew {s.BusinessName} for {Plural(s.RenewalYears, "year")}", "done", JourneyRecord.Sale, s.Id);
            if (!string.IsNullOrEmpty(s.ErrorMessage))
                Add(s.ProcessedAt ?? s.SyncedAt, "The Ontraport sale couldn't be renewed as it stood", "action", JourneyRecord.Sale, s.Id, s.ErrorMessage);
            Add(s.HandledAt, "Staff marked the Ontraport sale handled", "quiet", JourneyRecord.Sale, s.Id, s.HandledNote);
        }

        var runIds = g.Runs.ToList();
        var runs = await db.RenewalRequests.AsNoTracking()
            .Include(r => r.SearchResult).Include(r => r.StripePayment)
            .Where(r => runIds.Contains(r.Id)).ToListAsync(ct);

        var orderIds = g.Orders.ToList();
        var orders = await db.RenewalOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToListAsync(ct);
        foreach (var o in orders)
        {
            // A website payment is told by its card payment below; a sale by the Ontraport line above.
            var paidByCard = o.Source == RenewalOrderSource.Wizard && runs.Any(r => r.Id == o.SourceId && r.StripePayment?.PaidAt != null);
            if (o.Source == RenewalOrderSource.Manual)
                Add(o.PaidAt, $"Staff recorded a payment of ${o.AmountPaid:0.00} to renew {o.BusinessName} for {Plural(o.Years, "year")}", "done", JourneyRecord.Order, o.Id);
            else if (o.Source == RenewalOrderSource.Wizard && !paidByCard)
                Add(o.PaidAt, $"Paid ${o.AmountPaid:0.00} to renew {o.BusinessName} for {Plural(o.Years, "year")}", "done", JourneyRecord.Order, o.Id);

            if (o.Outcome is { } outcome)
                Add(o.OutcomeAt, $"{(o.OutcomeBy is { Length: > 0 } by ? by : "Staff")} closed it: {Words(outcome.ToString())}", "quiet", JourneyRecord.Order, o.Id,
                    string.Join(". ", new[] { o.Evidence, o.OutcomeReference is { Length: > 0 } reference ? $"Reference {reference}" : null }.Where(x => !string.IsNullOrWhiteSpace(x))));
            if (o.Note is { Length: > 0 } note)
                Add(o.StageChangedAt, "Note on the renewal", "quiet", JourneyRecord.Order, o.Id, note);
        }

        foreach (var r in runs)
        {
            var name = r.SearchResult.BusinessName;
            if (r.StripePayment?.PaidAt is { } paid)
            {
                var card = r.StripePayment.CardLast4 is { Length: > 0 } last4 ? $" ({r.StripePayment.CardBrand} ending {last4})" : "";
                Add(paid, $"Paid ${r.Amount:0.00} by card{card} to renew {name} for {Plural(r.RenewalYears, "year")}", "done", JourneyRecord.Run, r.Id);
            }

            Add(r.InitiatedAt, $"Renewtron started renewing {name} at ASIC{(r.IsTest ? " (test: not sent to ASIC)" : "")}", "working", JourneyRecord.Run, r.Id);

            if (r.BpayReference is { Length: > 0 })
                Add(r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt, $"ASIC took the renewal of {name}, to be paid by BPAY", "waiting", JourneyRecord.Run, r.Id,
                    $"Biller {r.BpayBillerCode}, reference {r.BpayReference}{(r.TransactionReference is { Length: > 0 } t ? $", transaction {t}" : "")}");
            else if (r.Status == RenewalStatus.Completed)
                Add(r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt, $"ASIC renewed {name}", "done", JourneyRecord.Run, r.Id,
                    r.TransactionReference is { Length: > 0 } t ? $"Transaction {t}" : null);

            if (r.BpayPaidAt is { } bpayPaid)
                Add(bpayPaid, $"Paid ASIC's BPAY bill for {name}{(r.BpayAmount is { } a ? $": ${a:0.00}" : "")}", "done", JourneyRecord.Run, r.Id,
                    r.AirwallexTransferId is { Length: > 0 } transfer ? $"Airwallex transfer {transfer}" : null);
            else if (r.BpayPaymentError is { Length: > 0 } bpayError)
                Add(r.LastAttemptAt ?? r.InitiatedAt, $"Paying ASIC's BPAY bill for {name} failed{(r.BpayPaymentAttempts > 1 ? $" ({Plural(r.BpayPaymentAttempts, "try", "tries")})" : "")}", "action", JourneyRecord.Run, r.Id, bpayError);

            if (r.Status == RenewalStatus.Failed)
            {
                var tries = r.AttemptCount > 1 ? $" after {Plural(r.AttemptCount, "try", "tries")}" : "";
                var at = r.LastAttemptAt ?? r.InitiatedAt;
                var where = r.FailedAtStep is { Length: > 0 } step ? $" at \"{step}\"" : "";
                if (r.NextRetryAt is { } next && r.HandledAt == null)
                    Add(at, $"Not renewed yet{tries}; Renewtron tries again {Sydney(next):d MMM}", "waiting", JourneyRecord.Run, r.Id, r.ErrorMessage);
                else
                    Add(at, $"Renewing {name} at ASIC failed{where}{tries}", r.HandledAt == null ? "action" : "quiet", JourneyRecord.Run, r.Id, r.ErrorMessage);
            }
            else if (r.Status == RenewalStatus.Processing)
                Add(r.LastAttemptAt ?? r.InitiatedAt, $"Renewing {name} at ASIC now", "working", JourneyRecord.Run, r.Id);

            Add(r.HandledAt, "Staff marked the ASIC run handled", "quiet", JourneyRecord.Run, r.Id, r.HandledNote);
        }

        var emailRuns = runIds;
        foreach (var m in await db.OutboundMessages.AsNoTracking()
                     .Where(m => (m.LeadId != null && leadIds.Contains(m.LeadId.Value)) || (m.RenewalRequestId != null && emailRuns.Contains(m.RenewalRequestId.Value)))
                     .ToListAsync(ct))
        {
            var channel = m.Channel == OutboundChannel.Sms ? "Texted" : "Emailed";
            var what = m.Channel == OutboundChannel.Sms ? m.Template : $"\"{m.Subject}\"";
            if (m.Status == OutboundStatus.Failed)
                Add(m.LastAttemptAt ?? m.CreatedAt, $"{channel} {m.Recipient} {what}, but it wasn't sent", "action", JourneyRecord.Email, m.Id, m.ErrorMessage);
            else
            {
                var opened = m.OpenedAt != null ? " (opened)" : m.DeliveryStatus is { Length: > 0 } d ? $" ({Words(d).ToLowerInvariant()})" : "";
                Add(m.SentAt ?? m.CreatedAt, $"{channel} {m.Recipient} {what}{opened}", m.Status == OutboundStatus.Queued ? "waiting" : "quiet", JourneyRecord.Email, m.Id);
            }
        }

        // Oldest first; things at the same moment keep the order they were told in.
        return events.Select((e, i) => (e, i)).OrderBy(x => x.e.At).ThenBy(x => x.i).Select(x => x.e).ToList();
    }

    /* ───────────── Words ───────────── */

    private static string Plural(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    private static string FirstName(string fullName) =>
        fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "The visitor";

    private static readonly TimeZoneInfo SydneyZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
    private static DateTime Sydney(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SydneyZone);

    /// <summary>"RenewedElsewhere" → "renewed elsewhere".</summary>
    public static string Words(string pascal) =>
        Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

    /// <summary>Up to three, then "and N more".</summary>
    private static string Join(IEnumerable<string?> parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return list.Count <= 3 ? string.Join(", ", list) : $"{string.Join(", ", list.Take(3))} and {list.Count - 3} more";
    }
}
