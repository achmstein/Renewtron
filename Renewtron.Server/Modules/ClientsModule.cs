using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>
/// Admin view of clients: one row per person with all their business names, and the tools to
/// build the records (dry run, then a real run), merge duplicates and move names between clients.
/// </summary>
public sealed class ClientsModule : ICarterModule
{
    public const string RecurringJobId = "client-link";

    public sealed record RunRequest(bool DryRun);
    public sealed record MoveNameRequest(Guid ClientId);
    public sealed record NameStatusRequest(string Status);
    public sealed record NotesRequest(string? Notes);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/clients").RequireAuthorization().WithTags("Admin.Clients");

        group.MapGet("/", async (ApplicationDbContext db, string? search = null, string? filter = null, int take = 200) =>
        {
            take = Math.Clamp(take, 1, 500);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            IQueryable<Client> q = db.Clients.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                var digits = AsicKeyRequestService.DigitsOnly(term);
                q = q.Where(c =>
                    c.FullName.ToLower().Contains(term)
                    || c.Emails.Any(e => e.Email.Contains(term))
                    || c.BusinessNames.Any(n => n.Name.ToLower().Contains(term))
                    || (digits.Length >= 6 && c.Abns.Any(a => a.Abn.Contains(digits)))
                    || (digits.Length >= 6 && c.MobileNumber != null && c.MobileNumber.Contains(digits)));
            }
            q = filter switch
            {
                "duplicates" => q.Where(c => c.PossibleDuplicateOfId != null),
                "several" => q.Where(c => c.BusinessNames.Count(n => n.Status == ClientBusinessNameStatus.Active) > 1),
                "purchased" => q.Where(c => c.HasPurchased),
                _ => q,
            };

            var totalCount = await q.CountAsync();
            var items = await q
                .OrderByDescending(c => c.LastSeenAt)
                .Take(take)
                .Select(c => new
                {
                    id = c.Id,
                    fullName = c.FullName,
                    email = c.Emails.OrderByDescending(e => e.IsPrimary).ThenBy(e => e.CreatedAt).Select(e => e.Email).FirstOrDefault(),
                    emailCount = c.Emails.Count,
                    mobileNumber = c.MobileNumber,
                    abns = c.Abns.OrderBy(a => a.CreatedAt).Select(a => a.Abn).ToList(),
                    activeNames = c.BusinessNames.Count(n => n.Status == ClientBusinessNameStatus.Active),
                    firstName = c.BusinessNames.Where(n => n.Status == ClientBusinessNameStatus.Active).OrderBy(n => n.Name).Select(n => n.Name).FirstOrDefault(),
                    nextDue = c.BusinessNames
                        .Where(n => n.Status == ClientBusinessNameStatus.Active && n.RenewalDueDate != null && n.RenewalDueDate >= today)
                        .Min(n => n.RenewalDueDate),
                    keys = c.BusinessNames.Count(n => n.AsicKey != null),
                    hasPurchased = c.HasPurchased,
                    isTest = c.IsTest,
                    possibleDuplicateOfId = c.PossibleDuplicateOfId,
                    possibleDuplicateOfName = c.PossibleDuplicateOf != null ? c.PossibleDuplicateOf.FullName : null,
                    lastSeenAt = c.LastSeenAt,
                })
                .ToListAsync();

            var stats = new
            {
                clients = await db.Clients.CountAsync(c => !c.IsTest),
                purchased = await db.Clients.CountAsync(c => !c.IsTest && c.HasPurchased),
                severalNames = await db.Clients.CountAsync(c => !c.IsTest && c.BusinessNames.Count(n => n.Status == ClientBusinessNameStatus.Active) > 1),
                names = await db.ClientBusinessNames.CountAsync(n => n.Status == ClientBusinessNameStatus.Active),
                possibleDuplicates = await db.Clients.CountAsync(c => c.PossibleDuplicateOfId != null),
            };
            return Results.Ok(new { totalCount, items, stats });
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var c = await db.Clients.AsNoTracking()
                .Include(x => x.Emails).Include(x => x.Abns).Include(x => x.BusinessNames).Include(x => x.PossibleDuplicateOf)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();

            var duplicates = await db.Clients.AsNoTracking().Where(x => x.PossibleDuplicateOfId == id)
                .Select(x => new { id = x.Id, fullName = x.FullName }).ToListAsync();
            var sales = await db.OntraportSales.AsNoTracking().Where(s => s.ClientId == id).OrderByDescending(s => s.SyncedAt)
                .Select(s => new { id = s.Id, businessName = s.BusinessName, status = s.Status.ToString(), syncedAt = s.SyncedAt, amountPaid = s.AmountPaid, renewalDueDate = s.RenewalDueDate, ontraportContactId = s.OntraportContactId })
                .ToListAsync();
            var leads = await db.Leads.AsNoTracking().Where(l => l.ClientId == id).OrderByDescending(l => l.CreatedAt)
                .Select(l => new { id = l.Id, createdAt = l.CreatedAt, outcome = l.Outcome.ToString(), abn = l.Abn, email = l.Email })
                .ToListAsync();
            var renewals = await db.RenewalRequests.AsNoTracking().Where(r => r.ClientId == id).OrderByDescending(r => r.InitiatedAt)
                .Select(r => new { id = r.Id, businessName = r.SearchResult.BusinessName, status = r.Status.ToString(), initiatedAt = r.InitiatedAt, completedAt = r.CompletedAt, amount = r.Amount, renewalYears = r.RenewalYears, source = r.Source.ToString() })
                .ToListAsync();
            var keyRequests = await db.AsicKeyRequests.AsNoTracking().Where(r => r.ClientId == id).OrderByDescending(r => r.CreatedAt)
                .Select(r => new { id = r.Id, businessName = r.BusinessName, status = r.Status.ToString(), createdAt = r.CreatedAt, asicReferenceNumber = r.AsicReferenceNumber })
                .ToListAsync();
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
                    .Select(e => new { id = e.Id, email = e.Email, isPrimary = e.IsPrimary, ontraportContactId = e.OntraportContactId, source = e.Source }),
                abns = c.Abns.OrderBy(a => a.CreatedAt).Select(a => new { abn = a.Abn, registerCheckedAt = a.RegisterCheckedAt }),
                businessNames = c.BusinessNames
                    .OrderBy(n => n.Status).ThenBy(n => n.RenewalDueDate ?? DateOnly.MaxValue).ThenBy(n => n.Name)
                    .Select(n => new
                    {
                        id = n.Id, name = n.Name, abn = n.Abn, registrationDate = n.RegistrationDate,
                        asicAccountNumber = n.AsicAccountNumber, renewalDueDate = n.RenewalDueDate, dueDateSource = n.DueDateSource,
                        asicKey = n.AsicKey, asicKeyReceivedAt = n.AsicKeyReceivedAt,
                        lastRenewedAt = n.LastRenewedAt, lastRenewalYears = n.LastRenewalYears,
                        status = n.Status.ToString(), source = n.Source,
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

        group.MapPost("/business-names/{nameId:guid}/move", async (Guid nameId, MoveNameRequest body, ApplicationDbContext db) =>
        {
            var name = await db.ClientBusinessNames.FirstOrDefaultAsync(n => n.Id == nameId);
            if (name == null) return Results.NotFound();
            if (!await db.Clients.AnyAsync(c => c.Id == body.ClientId)) return Results.Problem(detail: "Client not found.", statusCode: 400);
            name.ClientId = body.ClientId;
            name.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
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
