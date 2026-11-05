using System.Linq.Expressions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// The admin's work queues, defined once so the sidebar badges, list filters and stats agree on
/// what "needs a person" means. The admin's status vocabulary (frontend/src/admin/_status.tsx)
/// reads the same way.
/// </summary>
public static class WorkQueues
{
    /// <summary>
    /// A renewal that failed and nothing will retry on its own: no retry scheduled, and not
    /// turned away only because ASIC's renewal window hasn't opened.
    /// </summary>
    public static readonly Expression<Func<RenewalRequest, bool>> RenewalNeedsPerson = r =>
        r.Status == RenewalStatus.Failed
        && r.NextRetryAt == null
        && (r.ErrorCategory == null || r.ErrorCategory != RenewalErrorCategories.NotDueYet);

    /// <summary>
    /// Key letters that need a person: everything except those read and saved (to Ontraport or
    /// to the client) and those still being read.
    /// </summary>
    public static readonly AsicKeyNotificationStatus[] KeyLetterSettled =
    [
        AsicKeyNotificationStatus.Pending,
        AsicKeyNotificationStatus.Completed,
        AsicKeyNotificationStatus.SavedToClient,
    ];
}
