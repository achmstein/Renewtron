using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>The client a record belongs to, as record pages show it in their breadcrumb.</summary>
public sealed record ClientRef(Guid Id, string FullName);

internal static class ClientRefs
{
    public static Task<ClientRef?> ByIdAsync(ApplicationDbContext db, Guid? clientId) =>
        clientId is { } id
            ? db.Clients.AsNoTracking().Where(c => c.Id == id).Select(c => new ClientRef(c.Id, c.FullName)).FirstOrDefaultAsync()
            : Task.FromResult<ClientRef?>(null);

    /// <summary>
    /// For a record that only knows its ABN: the ABN's reminder contact, else its earliest
    /// contact who isn't a suspected duplicate.
    /// </summary>
    public static async Task<ClientRef?> ByAbnAsync(ApplicationDbContext db, string? abn)
    {
        var digits = Helpers.NormalizeAbn(abn);
        if (digits.Length != 11) return null;
        return await db.ClientAbns.AsNoTracking()
            .Where(a => a.Abn == digits && a.Client != null && !a.Client.IsTest)
            .OrderByDescending(a => a.IsReminderContact)
            .ThenBy(a => a.Client!.PossibleDuplicateOfId != null)
            .ThenBy(a => a.CreatedAt)
            .Select(a => new ClientRef(a.ClientId, a.Client!.FullName))
            .FirstOrDefaultAsync();
    }
}
