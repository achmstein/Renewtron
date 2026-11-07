using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>
/// Admin view of clients: one row per person with all their business names, and the tools to
/// build the records (dry run, then a real run), merge duplicates and choose who on a shared
/// ABN gets its reminders.
/// </summary>
public sealed class ClientsModule : ICarterModule
{
    public const string RecurringJobId = "client-link";

    public sealed record RunRequest(bool DryRun);
    public sealed record ReminderContactRequest(Guid ClientId);
    public sealed record NameStatusRequest(string Status);
    public sealed record NotesRequest(string? Notes);

    /// <summary>
    /// Clients with more than one active name across their ABNs, as one grouped query (SQL
    /// Server does this far faster than a count per client row).
    /// </summary>
    private static IQueryable<Guid> SeveralNamesClientIds(ApplicationDbContext db) =>
        from a in db.ClientAbns
        join n in db.ClientBusinessNames on a.Abn equals n.Abn
        where n.Status == ClientBusinessNameStatus.Active
        group n by a.ClientId into g
        where g.Count() > 1
        select g.Key;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/clients").RequireAuthorization().WithTags("Admin.Clients");

        // One page of clients. The header numbers are a separate request (/stats) so changing a
        // filter or page doesn't recount them. `take` is the search box's "first N" shortcut.
        group.MapGet("/", async (ApplicationDbContext db, string? search = null, string? filter = null, int page = 1, int pageSize = 25, int? take = null) =>
        {
            if (take is { } t) { pageSize = t; page = 1; }
            pageSize = Math.Clamp(pageSize, 1, 500);
            page = Math.Max(1, page);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            IQueryable<Client> q = db.Clients.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                var digits = AsicKeyRequestService.DigitsOnly(term);
                q = q.Where(c =>
                    c.FullName.ToLower().Contains(term)
                    || c.Emails.Any(e => e.Email.Contains(term))
                    || c.Abns.Any(a => a.Holder!.BusinessNames.Any(n => n.Name.ToLower().Contains(term)))
                    || (digits.Length >= 6 && c.Abns.Any(a => a.Abn.Contains(digits)))
                    || (digits.Length >= 6 && c.MobileNumber != null && c.MobileNumber.Contains(digits)));
            }
            var several = SeveralNamesClientIds(db);
            q = filter switch
            {
                "duplicates" => q.Where(c => c.PossibleDuplicateOfId != null),
                "several" => q.Where(c => several.Contains(c.Id)),
                "purchased" => q.Where(c => c.HasPurchased),
                _ => q,
            };

            var totalCount = await q.CountAsync();
            var items = await q
                .OrderByDescending(c => c.LastSeenAt).ThenBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    id = c.Id,
                    fullName = c.FullName,
                    email = c.Emails.OrderByDescending(e => e.IsPrimary).ThenBy(e => e.CreatedAt).Select(e => e.Email).FirstOrDefault(),
                    emailCount = c.Emails.Count,
                    mobileNumber = c.MobileNumber,
                    abns = c.Abns.OrderBy(a => a.CreatedAt).Select(a => a.Abn).ToList(),
                    activeNames = c.Abns.SelectMany(a => a.Holder!.BusinessNames).Count(n => n.Status == ClientBusinessNameStatus.Active),
                    firstName = c.Abns.SelectMany(a => a.Holder!.BusinessNames).Where(n => n.Status == ClientBusinessNameStatus.Active).OrderBy(n => n.Name).Select(n => n.Name).FirstOrDefault(),
                    nextDue = c.Abns.SelectMany(a => a.Holder!.BusinessNames)
                        .Where(n => n.Status == ClientBusinessNameStatus.Active && n.RenewalDueDate != null && n.RenewalDueDate >= today)
                        .Min(n => n.RenewalDueDate),
                    keys = c.Abns.SelectMany(a => a.Holder!.BusinessNames).Count(n => n.AsicKey != null),
                    // On an ABN with someone else who gets its reminders.
                    sharesAbn = c.Abns.Any(a => !a.IsReminderContact),
                    hasPurchased = c.HasPurchased,
                    isTest = c.IsTest,
                    possibleDuplicateOfId = c.PossibleDuplicateOfId,
                    possibleDuplicateOfName = c.PossibleDuplicateOf != null ? c.PossibleDuplicateOf.FullName : null,
                    lastSeenAt = c.LastSeenAt,
                })
                .ToListAsync();

            return Results.Ok(new { totalCount, page, pageSize, items });
        });

        group.MapGet("/stats", async (ApplicationDbContext db) =>
        {
            var several = SeveralNamesClientIds(db);
            return Results.Ok(new
            {
                clients = await db.Clients.CountAsync(c => !c.IsTest),
                purchased = await db.Clients.CountAsync(c => !c.IsTest && c.HasPurchased),
                severalNames = await db.Clients.CountAsync(c => !c.IsTest && several.Contains(c.Id)),
                names = await db.ClientBusinessNames.CountAsync(n => n.Status == ClientBusinessNameStatus.Active),
                possibleDuplicates = await db.Clients.CountAsync(c => c.PossibleDuplicateOfId != null),
            });
        });

        // The client a record belongs to, for the "Client" link on lead, renewal, key request and
        // key letter pages. A key letter sits on a business name, so it goes to the name's ABN's
        // reminder contact.
        group.MapGet("/lookup", async (string kind, Guid id, ApplicationDbContext db) =>
        {
            Guid? clientId = kind switch
            {
                "lead" => await db.Leads.Where(x => x.Id == id).Select(x => x.ClientId).FirstOrDefaultAsync(),
                "renewal" => await db.RenewalRequests.Where(x => x.Id == id).Select(x => x.ClientId).FirstOrDefaultAsync(),
                "sale" => await db.OntraportSales.Where(x => x.Id == id).Select(x => x.ClientId).FirstOrDefaultAsync(),
                "keyRequest" => await db.AsicKeyRequests.Where(x => x.Id == id).Select(x => x.ClientId).FirstOrDefaultAsync(),
                "keyLetter" => await (
                    from n in db.AsicKeyNotifications
                    where n.Id == id
                    join b in db.ClientBusinessNames on n.ClientBusinessNameId equals b.Id
                    join a in db.ClientAbns on b.Abn equals a.Abn
                    orderby a.IsReminderContact descending, a.CreatedAt
                    select (Guid?)a.ClientId).FirstOrDefaultAsync(),
                _ => null,
            };
            if (clientId == null) return Results.NotFound();
            var client = await db.Clients.AsNoTracking().Where(c => c.Id == clientId)
                .Select(c => new { id = c.Id, fullName = c.FullName, possibleDuplicate = c.PossibleDuplicateOfId != null })
                .FirstOrDefaultAsync();
            return client == null ? Results.NotFound() : Results.Ok(client);
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var c = await db.Clients.AsNoTracking()
                .Include(x => x.Emails).Include(x => x.Abns).Include(x => x.PossibleDuplicateOf)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();

            // Each ABN with its names and everyone who deals with us about it.
            var abnList = c.Abns.Select(a => a.Abn).ToList();
            var holders = await db.AbnHolders.AsNoTracking()
                .Where(h => abnList.Contains(h.Abn))
                .Include(h => h.BusinessNames)
                .Include(h => h.Contacts).ThenInclude(a => a.Client)
                .AsSplitQuery()
                .ToDictionaryAsync(h => h.Abn);

            var duplicates = await db.Clients.AsNoTracking().Where(x => x.PossibleDuplicateOfId == id)
                .Select(x => new { id = x.Id, fullName = x.FullName }).ToListAsync();
            var sales = await db.OntraportSales.AsNoTracking().Where(s => s.ClientId == id).OrderByDescending(s => s.SyncedAt)
                .Select(s => new { id = s.Id, businessName = s.BusinessName, status = s.Status.ToString(), syncedAt = s.SyncedAt, amountPaid = s.AmountPaid, renewalDueDate = s.RenewalDueDate, ontraportContactId = s.OntraportContactId })
                .ToListAsync();
            var leads = await db.Leads.AsNoTracking().Where(l => l.ClientId == id).OrderByDescending(l => l.CreatedAt)
                .Select(l => new { id = l.Id, createdAt = l.CreatedAt, outcome = l.Outcome.ToString(), abn = l.Abn, email = l.Email })
                .ToListAsync();
            var renewals = await db.RenewalRequests.AsNoTracking().Where(r => r.ClientId == id).OrderByDescending(r => r.InitiatedAt)
                .Select(r => new { id = r.Id, businessName = r.SearchResult.BusinessName, status = r.Status.ToString(), initiatedAt = r.InitiatedAt, completedAt = r.CompletedAt, amount = r.Amount, renewalYears = r.RenewalYears, source = r.Source.ToString(), bpayReference = r.BpayReference, bpayPaidAt = r.BpayPaidAt })
                .ToListAsync();
            var keyRequests = await db.AsicKeyRequests.AsNoTracking().Where(r => r.ClientId == id).OrderByDescending(r => r.CreatedAt)
                .Select(r => new { id = r.Id, businessName = r.BusinessName, status = r.Status.ToString(), createdAt = r.CreatedAt, asicReferenceNumber = r.AsicReferenceNumber })
                .ToListAsync();
            // The latest "link to our ASIC account" run for each of the client's names.
            var nameIds = holders.Values.SelectMany(h => h.BusinessNames).Select(n => n.Id).ToList();
            var portalRuns = (await db.AsicPortalLinks.AsNoTracking().Where(r => nameIds.Contains(r.ClientBusinessNameId))
                    .Select(r => new { r.Id, r.ClientBusinessNameId, Status = r.Status.ToString(), r.CreatedAt, r.FinishedAt })
                    .ToListAsync())
                .GroupBy(r => r.ClientBusinessNameId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());
            var messages = await db.OutboundMessages.AsNoTracking().Where(m => m.ClientId == id).OrderByDescending(m => m.CreatedAt).Take(50)
                .Select(m => new { id = m.Id, template = m.Template, subject = m.Subject, status = m.Status.ToString(), createdAt = m.CreatedAt, recipient = m.Recipient })
                .ToListAsync();

            return Results.Ok(new
            {
                id = c.Id,
                fullName = c.FullName,
                mobileNumber = c.MobileNumber,
                dateOfBirth = c.DateOfBirth,
                address = AsicKeyRequestService.JoinAddress(c.Address, c.Suburb, c.State, c.Postcode),
                hasTfn = c.TfnEncrypted != null,
                hasPurchased = c.HasPurchased,
                reminderOptIn = c.ReminderOptIn,
                unsubscribedAt = c.UnsubscribedAt,
                isTest = c.IsTest,
                notes = c.Notes,
                createdAt = c.CreatedAt,
                lastSeenAt = c.LastSeenAt,
                possibleDuplicateOf = c.PossibleDuplicateOf == null ? null : new { id = c.PossibleDuplicateOf.Id, fullName = c.PossibleDuplicateOf.FullName },
                duplicates,
                emails = c.Emails.OrderByDescending(e => e.IsPrimary).ThenBy(e => e.CreatedAt)
                    .Select(e => new { id = e.Id, email = e.Email, isPrimary = e.IsPrimary, ontraportContactId = e.OntraportContactId, source = e.Source.ToString() }),
                abns = c.Abns.OrderBy(a => a.CreatedAt).Select(a =>
                {
                    var h = holders.GetValueOrDefault(a.Abn);
                    return new
                    {
                        abn = a.Abn,
                        isReminderContact = a.IsReminderContact,
                        registerCheckedAt = h?.RegisterCheckedAt,
                        contacts = (h?.Contacts ?? []).Where(x => x.ClientId != c.Id).OrderBy(x => x.CreatedAt)
                            .Select(x => new { clientId = x.ClientId, fullName = x.Client?.FullName ?? "", isReminderContact = x.IsReminderContact }),
                        businessNames = (h?.BusinessNames ?? [])
                            .OrderBy(n => n.Status).ThenBy(n => n.RenewalDueDate ?? DateOnly.MaxValue).ThenBy(n => n.Name)
                            .Select(n => new
                            {
                                id = n.Id, name = n.Name, abn = n.Abn, registrationDate = n.RegistrationDate,
                                asicAccountNumber = n.AsicAccountNumber, renewalDueDate = n.RenewalDueDate, dueDateSource = n.DueDateSource?.ToString(),
                                asicKey = n.AsicKey, asicKeyReceivedAt = n.AsicKeyReceivedAt,
                                portalLinkedAt = n.PortalLinkedAt,
                                portalLink = portalRuns.GetValueOrDefault(n.Id) is { } pr
                                    ? new { id = pr.Id, status = pr.Status, createdAt = pr.CreatedAt, finishedAt = pr.FinishedAt }
                                    : null,
                                lastRenewedAt = n.LastRenewedAt, lastRenewalYears = n.LastRenewalYears,
                                status = n.Status.ToString(), source = n.Source.ToString(),
                            }),
                    };
                }),
                sales, leads, renewals, keyRequests, messages,
            });
        });

        // ---- building the records ---------------------------------------------------

        group.MapGet("/link-status", async (ApplicationDbContext db, ClientLinkState state) =>
        {
            var (last, runningSince, runningDry) = state.Snapshot();
            var hasClients = await db.Clients.AnyAsync();
            return Results.Ok(new { hasClients, running = runningSince != null, runningSince, runningDry, last });
        });

        // Runs in the background (a full backfill takes longer than a request may stay open);
        // the page polls /link-status for the result.
        group.MapPost("/link", (RunRequest body, IBackgroundJobClient jobs, ClientLinkState state) =>
        {
            var (_, runningSince, _) = state.Snapshot();
            if (runningSince != null) return Results.Problem(detail: "A run is already going.", statusCode: 409);
            state.Started(body.DryRun);
            jobs.Enqueue<IClientLinkService>(s => s.LinkAsync(body.DryRun, CancellationToken.None));
            return Results.Accepted();
        });

        // ---- staff decisions --------------------------------------------------------

        group.MapPost("/{id:guid}/merge-into/{targetId:guid}", async (Guid id, Guid targetId, IClientLinkService service, CancellationToken ct) =>
        {
            try
            {
                await service.MergeAsync(id, targetId, ct);
                return Results.Ok(new { id = targetId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400);
            }
        });

        group.MapPost("/{id:guid}/not-duplicate", async (Guid id, ApplicationDbContext db) =>
        {
            var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();
            c.PossibleDuplicateOfId = null;
            c.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPut("/{id:guid}/notes", async (Guid id, NotesRequest body, ApplicationDbContext db) =>
        {
            var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();
            var notes = (body.Notes ?? "").Trim();
            c.Notes = notes.Length == 0 ? null : notes.Length <= 2000 ? notes : notes[..2000];
            c.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Hands an ABN's reminders (and paying for its names) to another of its contacts.
        group.MapPost("/abns/{abn}/reminder-contact", async (string abn, ReminderContactRequest body, ApplicationDbContext db) =>
        {
            var links = await db.ClientAbns.Where(a => a.Abn == abn).ToListAsync();
            var chosen = links.FirstOrDefault(a => a.ClientId == body.ClientId);
            if (chosen == null) return Results.Problem(detail: "That client isn't a contact on this ABN.", statusCode: 400);
            if (chosen.IsReminderContact) return Results.Ok();

            await using var tx = await db.Database.BeginTransactionAsync();
            // Cleared first: the database allows one reminder contact per ABN at a time.
            foreach (var a in links) a.IsReminderContact = false;
            await db.SaveChangesAsync();
            chosen.IsReminderContact = true;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok();
        });

        group.MapPost("/business-names/{nameId:guid}/status", async (Guid nameId, NameStatusRequest body, ApplicationDbContext db) =>
        {
            if (!Enum.TryParse<ClientBusinessNameStatus>(body.Status, ignoreCase: true, out var status))
                return Results.Problem(detail: "Unknown status.", statusCode: 400);
            var name = await db.ClientBusinessNames.FirstOrDefaultAsync(n => n.Id == nameId);
            if (name == null) return Results.NotFound();
            name.Status = status;
            name.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
