using Renewtron.Data;

namespace Renewtron.Abstractions;

public interface IOntraportSalesService
{
    Task<List<OntraportSale>> SyncSalesAsync();
    Task ProcessEligibleRenewalsAsync();

    /// <summary>
    /// Writes the outcome of a renewal attempt back onto the Ontraport contact: the renewal
    /// status field, and — on success only — the rolled-forward renewal due date.
    /// Returns true only when every Ontraport write was acknowledged; callers use this to
    /// keep the outbox row pending for retry.
    /// </summary>
    Task<bool> SyncRenewalOutcomeAsync(string contactId, OntraportRenewalOutcome outcome, DateTime? newRenewalDueDate = null);

    /// <summary>
    /// Retries pending OntraportSyncOutbox rows (renewal outcomes whose write-back to
    /// Ontraport failed earlier). Returns how many were sent this run.
    /// </summary>
    Task<int> ProcessSyncOutboxAsync();

    /// <summary>
    /// Contacts whose <paramref name="fieldId"/> equals <paramref name="value"/>. Ontraport's
    /// "=" (and LIKE) is case-SENSITIVE: "CAROUSEL INTERIORS" won't find "Carousel Interiors" —
    /// use <see cref="SearchContactsAsync"/> for that. Each result carries id, firstname,
    /// lastname, email and the business-name/ABN fields. Empty means no match; an API failure throws.
    /// </summary>
    Task<List<Dictionary<string, string?>>> FindContactsByFieldAsync(string fieldId, string value, int max = 25);

    /// <summary>
    /// Ontraport's case-insensitive free-text search (the same one as the contact list's search
    /// box) across a contact's fields. Loose by nature — callers filter the results. Same
    /// result shape and failure behaviour as <see cref="FindContactsByFieldAsync"/>.
    /// </summary>
    Task<List<Dictionary<string, string?>>> SearchContactsAsync(string text, int max = 50);

    /// <summary>Sets arbitrary fields on one contact. False when Ontraport rejected the write.</summary>
    Task<bool> UpdateContactFieldsAsync(string contactId, Dictionary<string, object> fields);

    /// <summary>
    /// Looks the field id up in the Contact object's metadata. Returns its display alias, or
    /// null when no such field exists. Throws when the metadata call itself fails.
    /// </summary>
    Task<string?> GetContactFieldAliasAsync(string fieldId);
}
