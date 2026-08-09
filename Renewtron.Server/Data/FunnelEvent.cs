namespace Renewtron.Data;

/// <summary>
/// One step a visitor reached in the public renewal wizard. Recorded server-side so the
/// drop-off report survives ad blockers and cookie banners that eat client-side pixels.
/// </summary>
public class FunnelEvent
{
    public Guid Id { get; set; }

    /// <summary>Stable per browser (localStorage) — lets us count people, not page loads.</summary>
    public string VisitorId { get; set; } = string.Empty;

    /// <summary>Per visit (sessionStorage).</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>One of <see cref="FunnelSteps"/>. Anything else is rejected at the endpoint.</summary>
    public string Step { get; set; } = string.Empty;

    public Guid? LeadId { get; set; }
    public Lead? Lead { get; set; }

    public string? Abn { get; set; }

    /// <summary>utm_source, or "ontraport" when they arrived on a prefilled renewal link.</summary>
    public string? Source { get; set; }

    /// <summary>Step-specific extra: the check outcome, a card decline message, and so on.</summary>
    public string? Detail { get; set; }

    public string? Path { get; set; }
    public string? Referrer { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// The steps the wizard reports. Keep in sync with frontend/src/lib/tracking.ts.
/// </summary>
public static class FunnelSteps
{
    public const string AbnViewed = "abn_viewed";
    public const string AbnSubmitted = "abn_submitted";
    public const string DetailsViewed = "details_viewed";
    public const string DetailsSubmitted = "details_submitted";
    public const string CheckStarted = "check_started";
    public const string CheckAvailable = "check_available";
    public const string CheckUnavailable = "check_unavailable";
    public const string CheckFailed = "check_failed";
    public const string SelectViewed = "select_viewed";
    public const string SelectSubmitted = "select_submitted";
    public const string PaymentViewed = "payment_viewed";
    public const string PaymentSubmitted = "payment_submitted";
    public const string PaymentFailed = "payment_failed";
    public const string RenewalComplete = "renewal_complete";

    /// <summary>
    /// The happy path, in order. The drop-off report walks this list, so a visitor who
    /// reaches step N without reporting step N-1 still counts at N.
    /// </summary>
    public static readonly (string Step, string Label)[] Ordered =
    [
        (AbnViewed, "Landed on ABN step"),
        (AbnSubmitted, "Entered ABN"),
        (DetailsViewed, "Reached details step"),
        (DetailsSubmitted, "Submitted details"),
        (CheckStarted, "ASIC check started"),
        (CheckAvailable, "Renewal available"),
        (SelectViewed, "Reached name selection"),
        (SelectSubmitted, "Selected names"),
        (PaymentViewed, "Reached payment"),
        (PaymentSubmitted, "Submitted payment"),
        (RenewalComplete, "Renewal complete"),
    ];

    /// <summary>Ways out of the funnel — reported alongside the main path, not inside it.</summary>
    public static readonly (string Step, string Label)[] Exits =
    [
        (CheckUnavailable, "Not eligible to renew"),
        (CheckFailed, "ASIC check errored"),
        (PaymentFailed, "Payment declined or errored"),
    ];

    private static readonly HashSet<string> KnownSteps =
        [.. Ordered.Select(s => s.Step), .. Exits.Select(s => s.Step)];

    /// <summary>
    /// How far through the flow each step sits. Exits slot in just after the step they
    /// follow, so "furthest step reached" ranks a declined payment ahead of merely
    /// reaching the payment page.
    /// </summary>
    private static readonly Dictionary<string, double> Ranks = BuildRanks();

    private static Dictionary<string, double> BuildRanks()
    {
        var ranks = new Dictionary<string, double>();
        for (var i = 0; i < Ordered.Length; i++)
            ranks[Ordered[i].Step] = i;

        ranks[CheckUnavailable] = ranks[CheckStarted] + 0.5;
        ranks[CheckFailed] = ranks[CheckStarted] + 0.4;
        ranks[PaymentFailed] = ranks[PaymentSubmitted] + 0.5;
        return ranks;
    }

    public static bool IsKnown(string? step) => step is not null && KnownSteps.Contains(step);

    public static double RankOf(string step) => Ranks.GetValueOrDefault(step, -1);

    public static string LabelFor(string step) =>
        Ordered.Concat(Exits).FirstOrDefault(s => s.Step == step).Label ?? step;
}
