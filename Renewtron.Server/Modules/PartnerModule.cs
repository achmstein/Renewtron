using System.Globalization;
using System.Text.RegularExpressions;
using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Identity;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// The Business Portal's API. Authenticated with the scoped partner key
/// (Security:PartnerApiKey), which the default policy rejects — so this module is the whole
/// of what the portal can reach. Renewtron stays the system of record for renewals, the
/// Ontraport sync and ASIC keys; the portal reads renewals (every status, so customers see
/// "in progress" and failures, not just completions), queues ASIC key requests, and pulls
/// keys back. No TFN, IP or card data leaves through here.
/// </summary>
public sealed class PartnerModule : ICarterModule
{
    public sealed record AsicKeyRequestBody(
        string? BusinessName, string? Abn, string? Email, string? GivenNames, string? FamilyName,
        string? Phone, string? ExternalReference);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/partner")
            .RequireAuthorization(ApiKeyAuthenticationHandler.PartnerPolicy)
            .WithTags("Partner");

        // ---- renewals ---------------------------------------------------------------
        group.MapGet("/renewals", async (ApplicationDbContext db, DateOnly? since, int page = 1, int pageSize = 200) =>
        {
            if (since is null) return Results.BadRequest(new { error = "since (yyyy-MM-dd) is required." });
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var from = since.Value.ToDateTime(TimeOnly.MinValue);

            var query = db.RenewalRequests.AsNoTracking().Where(r => r.InitiatedAt >= from);
            var total = await query.CountAsync();

            var rows = await query
                .OrderBy(r => r.InitiatedAt).ThenBy(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.Status, r.Source, r.RenewalYears, r.Amount, r.Email, r.MobileNumber, r.DateOfBirth,
                    r.InitiatedAt, r.CompletedAt, r.TransactionReference, r.ErrorCategory, r.NextRetryAt,
                    BusinessName = r.SearchResult.BusinessName,
                    Abn = r.SearchResult.SearchLog.Abn,
                    LeadName = r.Lead != null ? r.Lead.FullName : null,
                    LeadMobile = r.Lead != null ? r.Lead.MobileNumber : null,
                    LeadDob = r.Lead != null ? (DateOnly?)r.Lead.DateOfBirth : null,
                })
                .ToListAsync();

            // Ontraport and bulk renewals have no Lead; their contact lives on the sale / upload row.
            var ids = rows.Select(r => r.Id).ToList();
            var sales = await db.OntraportSales.AsNoTracking()
                .Where(s => s.RenewalRequestId != null && ids.Contains(s.RenewalRequestId.Value))
                .Select(s => new { Id = s.RenewalRequestId!.Value, s.ContactName, s.MobileNumber, s.DateOfBirth })
                .ToListAsync();
            var uploads = await db.BulkRenewalUploads.AsNoTracking()
                .Where(b => b.RenewalRequestId != null && ids.Contains(b.RenewalRequestId.Value))
                .Select(b => new { Id = b.RenewalRequestId!.Value, b.OwnerName })
                .ToListAsync();
            var saleBy = sales.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            var uploadBy = uploads.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());

            var items = rows.Select(r =>
            {
                saleBy.TryGetValue(r.Id, out var sale);
                uploadBy.TryGetValue(r.Id, out var upload);
                return new
                {
                    id = r.Id,
                    status = r.Status.ToString(),
                    source = r.Source.ToString(),
                    businessName = r.BusinessName,
                    abn = r.Abn,
                    renewalYears = r.RenewalYears,
                    amount = r.Amount,
                    email = r.Email,
                    fullName = FirstNonEmpty(r.LeadName, sale?.ContactName, upload?.OwnerName),
                    mobileNumber = FirstNonEmpty(r.MobileNumber, r.LeadMobile, sale?.MobileNumber),
                    dateOfBirth = r.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                  ?? (r.LeadDob is { } dob && dob != default ? dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null)
                                  ?? NormaliseDate(sale?.DateOfBirth),
                    initiatedAt = r.InitiatedAt,
                    completedAt = r.CompletedAt,
                    transactionReference = r.TransactionReference,
                    customerMessage = RenewalErrorClassifier.CustomerMessage(r.Status, r.ErrorCategory, r.NextRetryAt),
                    nextRetryAt = r.NextRetryAt,
                };
            }).ToList();

            return Results.Ok(new { totalCount = total, page, pageSize, items });
        });

        // ---- ASIC key requests ------------------------------------------------------
        group.MapPost("/asic-key-requests", async (
            AsicKeyRequestBody body, ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings) =>
        {
            var businessName = Regex.Replace(body.BusinessName ?? "", @"\s+", " ").Trim();
            var email = (body.Email ?? "").Trim();
            var given = (body.GivenNames ?? "").Trim();
            var family = (body.FamilyName ?? "").Trim();
            var phone = (body.Phone ?? "").Trim();
            if (businessName.Length == 0 || email.Length == 0 || given.Length == 0 || family.Length == 0 || phone.Length == 0)
                return Results.BadRequest(new { error = "businessName, email, givenNames, familyName and phone are required." });
            var abn = AsicKeyRequestService.DigitsOnly(body.Abn);
            var nameLower = businessName.ToLower();
            var emailLower = email.ToLower();

            // A key we already hold is handed straight back — no need to ask ASIC again.
            var known = await db.AsicKeyNotifications.AsNoTracking()
                .Where(n => n.AsicKey != null && n.AsicKey != "" && n.BusinessName != null)
                .Where(n => n.BusinessName!.ToLower() == nameLower || (abn != "" && n.Abn == abn))
                .OrderByDescending(n => n.ReceivedAt)
                .Select(n => n.AsicKey)
                .FirstOrDefaultAsync();
            if (known != null)
                return Results.Ok(new { id = (Guid?)null, status = "KeyAvailable", asicKey = (string?)known, createdAt = (DateTime?)null });

            // One live request per name and client: a repeat click returns the open one.
            var existing = await db.AsicKeyRequests.AsNoTracking()
                .Where(r => r.Status != AsicKeyRequestStatus.Failed
                            && r.BusinessName.ToLower() == nameLower
                            && r.Email.ToLower() == emailLower)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
            if (existing != null)
                return Results.Ok(new { id = (Guid?)existing.Id, status = existing.Status.ToString(), asicKey = (string?)null, createdAt = (DateTime?)existing.CreatedAt });

            var request = new AsicKeyRequest
            {
                Id = Guid.NewGuid(),
                GivenNames = given,
                FamilyName = family,
                Email = email,
                Phone = phone,
                Abn = abn,
                BusinessName = businessName,
                Source = "Portal",
                Status = AsicKeyRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };
            request.Question = AsicKeyRequestService.Render(request, settings.CurrentValue);
            db.AsicKeyRequests.Add(request);
            await db.SaveChangesAsync();

            return Results.Ok(new { id = (Guid?)request.Id, status = request.Status.ToString(), asicKey = (string?)null, createdAt = (DateTime?)request.CreatedAt });
        });

        group.MapGet("/asic-key-requests/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var row = await db.AsicKeyRequests.AsNoTracking()
                .Include(r => r.AsicKeyNotification)
                .FirstOrDefaultAsync(r => r.Id == id && r.Source == "Portal");
            if (row is null) return Results.NotFound();

            return Results.Ok(new
            {
                id = row.Id,
                status = row.Status.ToString(),
                businessName = row.BusinessName,
                createdAt = row.CreatedAt,
                submittedAt = row.SubmittedAt,
                keyReceivedAt = row.KeyReceivedAt,
                asicKey = row.Status == AsicKeyRequestStatus.KeyReceived ? row.AsicKeyNotification?.AsicKey : null,
            });
        });

        // Keys that arrived in the inbox since a point in time. The portal matches them to its
        // own business names by name / ABN, whether or not the portal asked for them.
        group.MapGet("/asic-keys", async (ApplicationDbContext db, DateTime? since) =>
        {
            if (since is null) return Results.BadRequest(new { error = "since (ISO date-time) is required." });
            var from = since.Value.Kind == DateTimeKind.Local ? since.Value.ToUniversalTime() : since.Value;

            var keys = await db.AsicKeyNotifications.AsNoTracking()
                .Where(n => n.AsicKey != null && n.AsicKey != "")
                .Where(n => n.ReceivedAt >= from || (n.ProcessedAt != null && n.ProcessedAt >= from))
                .OrderBy(n => n.ReceivedAt)
                .Take(500)
                .Select(n => new
                {
                    notificationId = n.Id,
                    businessName = n.BusinessName,
                    abn = n.Abn,
                    asicKey = n.AsicKey,
                    receivedAt = n.ReceivedAt,
                })
                .ToListAsync();
            return Results.Ok(keys);
        });
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Ontraport dates arrive in several shapes; only a parseable date is passed on.</summary>
    private static string? NormaliseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, out var unix) && unix > 0)
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-AU"), DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
}
