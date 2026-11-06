using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// Whether ASIC is refusing our card. ASIC declines it a whole batch at a time (a limit or the
/// bank's fraud check), not one renewal at a time: on 9–10 Sep, 29–30 Sep and 17–18 Oct 2026
/// every card payment was declined, and from 28 Oct every one was. So once the last
/// <see cref="DeclinesInARow"/> card payments have all been declined, the card counts as down:
/// renewals pay later by BPAY without trying it, and Today says so. A day after the last
/// decline the next renewal tries the card again, so a fixed card is noticed on its own.
/// </summary>
public static class AsicCardHealth
{
    public const int DeclinesInARow = 3;
    public static readonly TimeSpan TryAgainAfter = TimeSpan.FromHours(20);

    public sealed record State(bool Down, int Declines, DateTime? LastDeclineAt, DateTime? LastCardPaymentAt)
    {
        public static readonly State Fine = new(false, 0, null, null);

        /// <summary>For Today's alert; null while the card is fine.</summary>
        public string? Problem => Down
            ? $"ASIC has declined our card on the last {Declines} card payments, most recently {LastDeclineAt:d MMM HH:mm} UTC. "
              + "Renewals are paying later by BPAY instead. Check the card with the bank."
            : null;
    }

    public static async Task<State> CheckAsync(ApplicationDbContext db, DateTime now, CancellationToken ct = default)
    {
        // A failed card attempt sets CardFailureCount; the record's last run is roughly when.
        var declines = await db.RenewalRequests.AsNoTracking()
            .Where(r => !r.IsTest && r.CardFailureCount > 0 && r.LastAttemptAt != null)
            .OrderByDescending(r => r.LastAttemptAt)
            .Select(r => r.LastAttemptAt!.Value)
            .Take(DeclinesInARow)
            .ToListAsync(ct);
        // A card payment that went through: completed without a BPAY bill.
        var lastCardPayment = await db.RenewalRequests.AsNoTracking()
            .Where(r => !r.IsTest && r.Status == RenewalStatus.Completed && r.CompletedAt != null
                && (r.BpayReference == null || r.BpayReference == ""))
            .MaxAsync(r => r.CompletedAt, ct);

        if (declines.Count < DeclinesInARow) return State.Fine with { LastCardPaymentAt = lastCardPayment };
        var oldestOfRun = declines[^1];
        var latest = declines[0];
        var inARow = lastCardPayment is null || lastCardPayment < oldestOfRun;
        var down = inARow && now - latest < TryAgainAfter;
        if (!down) return new State(false, 0, latest, lastCardPayment);
        // How many in a row, for the alert: every decline since the last card payment.
        var since = lastCardPayment ?? DateTime.MinValue;
        var run = await db.RenewalRequests.AsNoTracking()
            .CountAsync(r => !r.IsTest && r.CardFailureCount > 0 && r.LastAttemptAt > since, ct);
        return new State(true, run, latest, lastCardPayment);
    }
}
