using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>The last link run, for the Clients page (one server process, so a singleton will do).</summary>
public sealed class ClientLinkState
{
    private readonly object _gate = new();
    private ClientLinkResult? _last;
    private DateTime? _runningSince;
    private bool _runningDry;

    public (ClientLinkResult? Last, DateTime? RunningSince, bool RunningDry) Snapshot()
    {
        lock (_gate) return (_last, _runningSince, _runningDry);
    }

    public void Started(bool dryRun) { lock (_gate) { _runningSince = DateTime.UtcNow; _runningDry = dryRun; } }
    public void Finished(ClientLinkResult result) { lock (_gate) { _last = result; _runningSince = null; } }
}

/// <summary>
/// Puts every record on its client. Matching, in order: same Ontraport contact → same email →
/// same ABN and date of birth → (same ABN only) a new client flagged as a possible duplicate.
/// A client's ABNs are read from our copy of the public register so all their business names
/// are on the record; each name has exactly one owner.
/// <para>Works in memory over the whole set, then saves once, so a dry run is the same code
/// with the save left out.</para>
/// </summary>
public sealed class ClientLinkService(
    ApplicationDbContext db,
    ClientLinkState state,
    IOptionsMonitor<TestModeSettings> testMode,
    ILogger<ClientLinkService> logger) : IClientLinkService
{
    /// <summary>ABNs re-read from the register per run, oldest check first, to catch new and cancelled names.</summary>
    private const int RegisterRefreshPerRun = 500;
    private static readonly TimeSpan RegisterRefreshAge = TimeSpan.FromDays(7);

    // ---- in-memory working set ----------------------------------------------------------
    private readonly Dictionary<string, Client> _byOntraport = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Client> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Client>> _byAbn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClientBusinessName> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Client> _byId = new();
    private readonly HashSet<string> _abnsToCheck = new(StringComparer.Ordinal);
    private ClientLinkResult _result = new();

    public async Task RunScheduledAsync()
    {
        if (!await db.Clients.AnyAsync()) return;
        await LinkAsync(dryRun: false);
    }

    public async Task<ClientLinkResult> LinkAsync(bool dryRun, CancellationToken ct = default)
    {
        _result = new ClientLinkResult { DryRun = dryRun, StartedAt = DateTime.UtcNow };
        state.Started(dryRun);
        var watch = Stopwatch.StartNew();
        try
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
            await LoadClientsAsync(ct);

            await LinkSalesAsync(ct);
            await LinkLeadsAsync(ct);
            await LinkKeyRequestsAsync(ct);

            // The register's names go on before bulk uploads and renewals look names up, and
            // again afterwards for ABNs those added; key letters then find any of them.
            QueueStaleAbns();
            await ReadRegisterAsync(ct);
            await LinkBulkUploadsAsync(ct);
            await LinkRenewalsAsync(ct);
            await LinkMessagesAsync(ct);
            await ReadRegisterAsync(ct);
            await LinkKeyLettersAsync(ct);

            if (!dryRun) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client link run failed (dry run: {DryRun})", dryRun);
            _result.Error = ex.Message;
        }
        finally
        {
            _result.FinishedAt = DateTime.UtcNow;
            state.Finished(_result);
        }

        logger.LogInformation(
            "Client link {Mode} in {Seconds:0.0}s: {Created} clients created ({Dups} possible duplicates), {Names} names added, {Sales} sales / {Leads} leads / {Renewals} renewals linked",
            dryRun ? "dry run" : "run", watch.Elapsed.TotalSeconds, _result.ClientsCreated, _result.PossibleDuplicates,
            _result.NamesAdded, _result.SalesLinked, _result.LeadsLinked, _result.RenewalsLinked);
        return _result;
    }

    private async Task LoadClientsAsync(CancellationToken ct)
    {
        var clients = await db.Clients
            .Include(c => c.Emails).Include(c => c.Abns).Include(c => c.BusinessNames)
            .AsSplitQuery()
            .ToListAsync(ct);
        foreach (var c in clients) Index(c);
    }

    private void Index(Client c)
    {
        _byId[c.Id] = c;
        foreach (var e in c.Emails)
        {
            _byEmail.TryAdd(e.Email, c);
            if (!string.IsNullOrEmpty(e.OntraportContactId)) _byOntraport.TryAdd(e.OntraportContactId, c);
        }
        foreach (var a in c.Abns) AbnList(a.Abn).Add(c);
        foreach (var n in c.BusinessNames) _names.TryAdd(NameKey(n.Abn, n.Name), n);
    }

    private List<Client> AbnList(string abn)
    {
        if (!_byAbn.TryGetValue(abn, out var list)) _byAbn[abn] = list = [];
        return list;
    }

    // ---- per record type ---------------------------------------------------------------

    private async Task LinkSalesAsync(CancellationToken ct)
    {
        var sales = await db.OntraportSales.Where(s => s.ClientId == null).OrderBy(s => s.SyncedAt).ToListAsync(ct);
        foreach (var s in sales)
        {
            var client = Resolve(new Identity
            {
                OntraportContactId = s.OntraportContactId,
                Email = s.Email,
                Abn = s.Abn,
                DateOfBirth = ParseDob(s.DateOfBirth),
                FullName = s.ContactName,
                Mobile = s.MobileNumber,
                Address = s.Address, Suburb = s.Suburb, State = s.State, Postcode = s.Postcode,
                TfnEncrypted = s.TfnEncrypted,
                HasPurchased = s.Status != OntraportSaleStatus.IneligibleForRenewal,
                SeenAt = s.SyncedAt,
                Source = "Ontraport",
            });
            if (client == null) { _result.Skipped++; continue; }

            s.ClientId = client.Id;
            _result.SalesLinked++;
            var name = AddName(client, s.Abn, s.BusinessName, "Ontraport");
            if (name == null) continue;
            s.ClientBusinessNameId = name.Id;
            if (s.RenewalDueDate is { } due)
            {
                if (s.Status == OntraportSaleStatus.RenewalCompleted && s.RenewalYears > 0)
                    SetDue(name, DateOnly.FromDateTime(due).AddYears(s.RenewalYears), "Renewal");
                else
                    SetDue(name, DateOnly.FromDateTime(due), "Ontraport");
            }
        }
    }

    private async Task LinkLeadsAsync(CancellationToken ct)
    {
        var leads = await db.Leads.Where(l => l.ClientId == null).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        foreach (var l in leads)
        {
            var client = Resolve(new Identity
            {
                OntraportContactId = l.OntraportContactId,
                Email = l.Email,
                Abn = l.Abn,
                DateOfBirth = l.DateOfBirth.Year > 1900 ? l.DateOfBirth : null,
                FullName = l.FullName,
                Mobile = l.MobileNumber,
                ReminderOptIn = l.ReminderOptIn,
                IsTest = l.IsTest,
                SeenAt = l.CreatedAt,
                Source = "Wizard",
            });
            if (client == null) { _result.Skipped++; continue; }
            l.ClientId = client.Id;
            _result.LeadsLinked++;
        }
    }

    private async Task LinkKeyRequestsAsync(CancellationToken ct)
    {
        var requests = await db.AsicKeyRequests.Where(r => r.ClientId == null).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var saleClients = await SaleClientsAsync(requests.Where(r => r.OntraportSaleId != null).Select(r => r.OntraportSaleId!.Value), ct);
        foreach (var r in requests)
        {
            Client? client = null;
            if (r.OntraportSaleId is { } saleId && saleClients.TryGetValue(saleId, out var cid)) _byId.TryGetValue(cid, out client);
            client ??= Resolve(new Identity
            {
                OntraportContactId = r.OntraportContactId,
                Email = r.Email,
                Abn = r.Abn,
                DateOfBirth = ParseDob(r.DateOfBirth),
                FullName = $"{r.GivenNames} {r.FamilyName}".Trim(),
                Mobile = r.Phone,
                IsTest = r.IsTest,
                SeenAt = r.CreatedAt,
                Source = "KeyRequest",
            });
            if (client == null) { _result.Skipped++; continue; }
            r.ClientId = client.Id;
            _result.KeyRequestsLinked++;
            AddName(client, r.Abn, r.BusinessName, "KeyRequest");
        }
    }

    /// <summary>Bulk uploads carry no email, so they join the client already holding the name (or the only client on the ABN).</summary>
    private async Task LinkBulkUploadsAsync(CancellationToken ct)
    {
        var uploads = await db.BulkRenewalUploads.Where(b => b.ClientId == null).OrderBy(b => b.UploadedAt).ToListAsync(ct);
        foreach (var b in uploads)
        {
            var abn = Digits(b.Abn);
            var name = FindName(abn, b.BusinessName);
            Client? client = name != null ? _byId.GetValueOrDefault(name.ClientId) : null;
            if (client == null && _byAbn.TryGetValue(abn, out var onAbn) && onAbn.Count == 1)
            {
                client = onAbn[0];
                name = AddName(client, abn, b.BusinessName, "BulkUpload");
            }
            if (client == null || name == null) { _result.Skipped++; continue; }

            b.ClientId = client.Id;
            b.ClientBusinessNameId = name.Id;
            client.HasPurchased = true;
            _result.BulkUploadsLinked++;
            if (b.RenewalDueDate is { } due)
            {
                if (b.Status == BulkRenewalStatus.RenewalCompleted && b.RenewalYears > 0)
                    SetDue(name, DateOnly.FromDateTime(due).AddYears(b.RenewalYears), "Renewal");
                else
                    SetDue(name, DateOnly.FromDateTime(due), "BulkUpload");
            }
        }
    }

    private async Task LinkRenewalsAsync(CancellationToken ct)
    {
        var renewals = await db.RenewalRequests
            .Where(r => r.ClientId == null)
            .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
            .OrderBy(r => r.InitiatedAt)
            .ToListAsync(ct);
        if (renewals.Count == 0) return;

        var ids = renewals.Select(r => r.Id).ToList();
        var leadIds = renewals.Where(r => r.LeadId != null).Select(r => r.LeadId!.Value).Distinct().ToList();
        // Tracked lists from the steps above already carry the ClientId they were just given.
        var leadClient = db.Leads.Local.Where(l => l.ClientId != null).ToDictionary(l => l.Id, l => l.ClientId!.Value);
        foreach (var row in await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id) && l.ClientId != null).Select(l => new { l.Id, l.ClientId }).ToListAsync(ct))
            leadClient.TryAdd(row.Id, row.ClientId!.Value);
        var viaSale = ClientsByRenewal(db.OntraportSales.Local.Where(s => s.RenewalRequestId != null).Select(s => (s.RenewalRequestId!.Value, s.ClientId)));
        foreach (var row in await db.OntraportSales.AsNoTracking().Where(s => s.RenewalRequestId != null && ids.Contains(s.RenewalRequestId.Value) && s.ClientId != null)
                     .Select(s => new { s.RenewalRequestId, s.ClientId }).ToListAsync(ct))
            viaSale.TryAdd(row.RenewalRequestId!.Value, row.ClientId!.Value);
        var viaBulk = ClientsByRenewal(db.BulkRenewalUploads.Local.Where(b => b.RenewalRequestId != null).Select(b => (b.RenewalRequestId!.Value, b.ClientId)));
        foreach (var row in await db.BulkRenewalUploads.AsNoTracking().Where(b => b.RenewalRequestId != null && ids.Contains(b.RenewalRequestId.Value) && b.ClientId != null)
                     .Select(b => new { b.RenewalRequestId, b.ClientId }).ToListAsync(ct))
            viaBulk.TryAdd(row.RenewalRequestId!.Value, row.ClientId!.Value);

        foreach (var r in renewals)
        {
            var abn = Digits(r.SearchResult?.SearchLog?.Abn);
            Client? client = null;
            if (r.LeadId is { } lid && leadClient.TryGetValue(lid, out var c1)) _byId.TryGetValue(c1, out client);
            if (client == null && viaSale.TryGetValue(r.Id, out var c2)) _byId.TryGetValue(c2, out client);
            if (client == null && viaBulk.TryGetValue(r.Id, out var c3)) _byId.TryGetValue(c3, out client);
            client ??= Resolve(new Identity
            {
                Email = r.Email,
                Abn = abn,
                DateOfBirth = r.DateOfBirth,
                Mobile = r.MobileNumber,
                IsTest = r.IsTest,
                SeenAt = r.InitiatedAt,
                Source = "Renewal",
            });
            if (client == null) { _result.Skipped++; continue; }

            r.ClientId = client.Id;
            _result.RenewalsLinked++;
            if (r.Status == RenewalStatus.Completed) client.HasPurchased = true;
            if (r.SearchResult == null) continue;

            var name = AddName(client, abn, r.SearchResult.BusinessName, "Renewal");
            if (name == null) continue;
            r.ClientBusinessNameId = name.Id;
            if (string.IsNullOrEmpty(name.RegistrationDate) && !string.IsNullOrWhiteSpace(r.SearchResult.RegistrationDate))
                name.RegistrationDate = Truncate(r.SearchResult.RegistrationDate.Trim(), 10);
            if (string.IsNullOrEmpty(name.AsicAccountNumber) && !string.IsNullOrWhiteSpace(r.SearchResult.AccountNumber))
                name.AsicAccountNumber = Truncate(r.SearchResult.AccountNumber.Trim(), 50);

            if (r.Status == RenewalStatus.Completed && r.CompletedAt is { } done)
            {
                if (name.LastRenewedAt == null || done > name.LastRenewedAt)
                {
                    name.LastRenewedAt = done;
                    name.LastRenewalYears = r.RenewalYears;
                    name.LastRenewalRequestId = r.Id;
                    Touch(name);
                }
                if (NextDueAfterRenewal(name.RegistrationDate, done, r.RenewalYears) is { } next)
                    SetDue(name, next, "Renewal");
            }
        }
    }

    private static Dictionary<Guid, Guid> ClientsByRenewal(IEnumerable<(Guid RenewalId, Guid? ClientId)> rows)
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var (renewalId, clientId) in rows)
            if (clientId is { } c) map.TryAdd(renewalId, c);
        return map;
    }

    private async Task LinkMessagesAsync(CancellationToken ct)
    {
        var messages = await db.OutboundMessages.Where(m => m.ClientId == null).ToListAsync(ct);
        if (messages.Count == 0) return;
        var leadClient = db.Leads.Local.Where(l => l.ClientId != null).ToDictionary(l => l.Id, l => l.ClientId!.Value);
        var leadIds = messages.Select(m => m.LeadId).Distinct().ToList();
        foreach (var row in await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id) && l.ClientId != null).Select(l => new { l.Id, l.ClientId }).ToListAsync(ct))
            leadClient.TryAdd(row.Id, row.ClientId!.Value);

        foreach (var m in messages)
        {
            if (!leadClient.TryGetValue(m.LeadId, out var cid)) { _result.Skipped++; continue; }
            m.ClientId = cid;
            _result.MessagesLinked++;
        }
    }

    /// <summary>Key letters land on the business name they're for, and the newest key is kept on the name.</summary>
    private async Task LinkKeyLettersAsync(CancellationToken ct)
    {
        var letters = await db.AsicKeyNotifications
            .Where(n => n.ClientBusinessNameId == null && n.AsicKey != null && n.AsicKey != "" && n.BusinessName != null)
            .OrderBy(n => n.ReceivedAt)
            .ToListAsync(ct);
        foreach (var n in letters)
        {
            var abn = Digits(n.Abn);
            var name = abn.Length == 11 ? FindName(abn, n.BusinessName!) : null;
            if (name == null)
            {
                // No ABN on the letter: by name alone, but only when exactly one client holds it.
                var matches = _names.Values.Where(x => string.Equals(Clean(x.Name), Clean(n.BusinessName), StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (matches.Count == 1) name = matches[0];
            }
            if (name == null) { _result.Skipped++; continue; }

            n.ClientBusinessNameId = name.Id;
            _result.KeyLettersLinked++;
            // "No Ontraport contact" stops being a problem once the key is on the client's name.
            if (n.Status == AsicKeyNotificationStatus.ContactNotFound)
            {
                n.Status = AsicKeyNotificationStatus.SavedToClient;
                n.ErrorMessage = null;
                _result.KeyLettersResolved++;
            }
            if (name.AsicKeyReceivedAt == null || n.ReceivedAt >= name.AsicKeyReceivedAt)
            {
                if (name.AsicKey != n.AsicKey) _result.KeysSet++;
                name.AsicKey = n.AsicKey;
                name.AsicKeyReceivedAt = n.ReceivedAt;
                name.AsicKeyNotificationId = n.Id;
                Touch(name);
            }
        }
    }

    private async Task<Dictionary<Guid, Guid>> SaleClientsAsync(IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.Distinct().ToList();
        var map = db.OntraportSales.Local.Where(s => s.ClientId != null && ids.Contains(s.Id)).ToDictionary(s => s.Id, s => s.ClientId!.Value);
        foreach (var row in await db.OntraportSales.AsNoTracking().Where(s => ids.Contains(s.Id) && s.ClientId != null).Select(s => new { s.Id, s.ClientId }).ToListAsync(ct))
            map.TryAdd(row.Id, row.ClientId!.Value);
        return map;
    }

    // ---- matching ----------------------------------------------------------------------

    private sealed class Identity
    {
        public string? OntraportContactId { get; init; }
        public string? Email { get; init; }
        public string? Abn { get; init; }
        public DateOnly? DateOfBirth { get; init; }
        public string? FullName { get; init; }
        public string? Mobile { get; init; }
        public string? Address { get; init; }
        public string? Suburb { get; init; }
        public string? State { get; init; }
        public string? Postcode { get; init; }
        public string? TfnEncrypted { get; init; }
        public bool HasPurchased { get; init; }
        public bool ReminderOptIn { get; init; }
        public bool IsTest { get; init; }
        public DateTime SeenAt { get; init; }
        public string Source { get; init; } = "";
    }

    private Client? Resolve(Identity id)
    {
        var email = CleanEmail(id.Email);
        var ontraportId = string.IsNullOrWhiteSpace(id.OntraportContactId) ? null : id.OntraportContactId.Trim();
        var abn = Digits(id.Abn);
        if (abn.Length != 11) abn = "";

        Client? client = null;
        if (ontraportId != null) _byOntraport.TryGetValue(ontraportId, out client);
        if (client == null && email != null) _byEmail.TryGetValue(email, out client);
        if (client == null && abn != "" && id.DateOfBirth is { } dob && _byAbn.TryGetValue(abn, out var sameAbn))
            client = sameAbn.FirstOrDefault(c => c.DateOfBirth == dob);

        if (client == null)
        {
            // Nothing to reach them by: leave the record unlinked rather than make a client we can't contact.
            if (email == null && ontraportId == null) return null;

            var dupOf = abn != "" && _byAbn.TryGetValue(abn, out var onAbn)
                ? onAbn.FirstOrDefault(c => c.PossibleDuplicateOfId == null) ?? onAbn.FirstOrDefault()
                : null;
            client = new Client
            {
                Id = Guid.NewGuid(),
                FullName = Truncate(Clean(id.FullName), 200),
                IsTest = id.IsTest || testMode.CurrentValue.IsTestEmail(email),
                PossibleDuplicateOfId = dupOf?.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeenAt = id.SeenAt,
            };
            db.Clients.Add(client);
            _byId[client.Id] = client;
            _result.ClientsCreated++;
            if (dupOf != null) _result.PossibleDuplicates++;
        }
        else if (!id.IsTest && !testMode.CurrentValue.IsTestEmail(email))
        {
            client.IsTest = false;
        }

        if (email != null) AddEmail(client, email, ontraportId, id.Source);
        else if (ontraportId != null) _byOntraport.TryAdd(ontraportId, client);
        if (abn != "") AddAbn(client, abn);
        FillProfile(client, id);
        return client;
    }

    private void AddEmail(Client client, string email, string? ontraportId, string source)
    {
        var existing = client.Emails.FirstOrDefault(e => string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            // An address another client already uses stays with them.
            if (_byEmail.ContainsKey(email)) { if (ontraportId != null) _byOntraport.TryAdd(ontraportId, client); return; }
            existing = new ClientEmail
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                Email = Truncate(email, 200),
                OntraportContactId = ontraportId,
                IsPrimary = client.Emails.Count == 0,
                Source = source,
                CreatedAt = DateTime.UtcNow,
            };
            client.Emails.Add(existing);
            db.ClientEmails.Add(existing);
            _byEmail[email] = client;
            _result.EmailsAdded++;
        }
        else if (existing.OntraportContactId == null && ontraportId != null)
        {
            existing.OntraportContactId = ontraportId;
        }
        if (ontraportId != null) _byOntraport.TryAdd(ontraportId, client);
    }

    private void AddAbn(Client client, string abn)
    {
        if (client.Abns.Any(a => a.Abn == abn)) return;
        var row = new ClientAbn { Id = Guid.NewGuid(), ClientId = client.Id, Abn = abn, CreatedAt = DateTime.UtcNow };
        client.Abns.Add(row);
        db.ClientAbns.Add(row);
        AbnList(abn).Add(client);
        _abnsToCheck.Add(abn);
        _result.AbnsAdded++;
    }

    private static void FillProfile(Client c, Identity id)
    {
        if (string.IsNullOrWhiteSpace(c.FullName) && !string.IsNullOrWhiteSpace(id.FullName)) c.FullName = Truncate(Clean(id.FullName), 200);
        if (string.IsNullOrWhiteSpace(c.MobileNumber) && !string.IsNullOrWhiteSpace(id.Mobile)) c.MobileNumber = Truncate(id.Mobile.Trim(), 20);
        c.DateOfBirth ??= id.DateOfBirth;
        if (string.IsNullOrWhiteSpace(c.Address) && string.IsNullOrWhiteSpace(c.Suburb) && (!string.IsNullOrWhiteSpace(id.Address) || !string.IsNullOrWhiteSpace(id.Suburb)))
        {
            c.Address = NullIfBlank(id.Address, 200);
            c.Suburb = NullIfBlank(id.Suburb, 100);
            c.State = NullIfBlank(id.State, 50);
            c.Postcode = NullIfBlank(id.Postcode, 10);
        }
        c.TfnEncrypted ??= id.TfnEncrypted;
        c.HasPurchased |= id.HasPurchased;
        c.ReminderOptIn |= id.ReminderOptIn;
        if (id.SeenAt > c.LastSeenAt) c.LastSeenAt = id.SeenAt;
        c.UpdatedAt = DateTime.UtcNow;
    }

    // ---- business names ----------------------------------------------------------------

    private ClientBusinessName? FindName(string abn, string? name) =>
        _names.GetValueOrDefault(NameKey(abn, name));

    /// <summary>The name's row: the existing one whoever owns it, or a new one on this client.</summary>
    private ClientBusinessName? AddName(Client client, string? abnRaw, string? nameRaw, string source, string? registrationDate = null)
    {
        var abn = Digits(abnRaw);
        var name = Truncate(Clean(nameRaw), 200);
        if (abn.Length != 11 || name.Length == 0) return null;

        var key = NameKey(abn, name);
        if (_names.TryGetValue(key, out var existing)) return existing;

        var row = new ClientBusinessName
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Abn = abn,
            Name = name,
            RegistrationDate = NullIfBlank(registrationDate, 10),
            Source = source,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        client.BusinessNames.Add(row);
        db.ClientBusinessNames.Add(row);
        _names[key] = row;
        _result.NamesAdded++;
        return row;
    }

    private static int Rank(string? source) => source == "Renewal" ? 2 : source == null ? 0 : 1;

    /// <summary>Our own renewals outrank Ontraport's field and bulk uploads; within a rank the later date wins.</summary>
    private void SetDue(ClientBusinessName name, DateOnly due, string source)
    {
        var take = name.RenewalDueDate == null
                   || Rank(source) > Rank(name.DueDateSource)
                   || (Rank(source) == Rank(name.DueDateSource) && due > name.RenewalDueDate);
        if (!take || (name.RenewalDueDate == due && name.DueDateSource == source)) return;
        name.RenewalDueDate = due;
        name.DueDateSource = source;
        Touch(name);
        _result.DueDatesSet++;
    }

    /// <summary>
    /// Renewals fall on the registration anniversary. The term just renewed is the
    /// anniversary closest to when it was paid; the next due date is that plus the years paid.
    /// </summary>
    public static DateOnly? NextDueAfterRenewal(string? registrationDate, DateTime renewedAt, int years)
    {
        if (years <= 0 || !DateOnly.TryParseExact((registrationDate ?? "").Trim(), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var reg))
            return null;
        var paid = DateOnly.FromDateTime(renewedAt);
        var nearest = Enumerable.Range(paid.Year - 1, 3)
            .Where(y => y > reg.Year)
            .Select(y => reg.AddYears(y - reg.Year))
            .OrderBy(d => Math.Abs(d.DayNumber - paid.DayNumber))
            .Cast<DateOnly?>()
            .FirstOrDefault();
        return nearest?.AddYears(years);
    }

    private void QueueStaleAbns()
    {
        var before = DateTime.UtcNow - RegisterRefreshAge;
        var all = _byId.Values.SelectMany(c => c.Abns).ToList();
        // Never-checked ABNs all go; ones checked before are refreshed a batch at a time.
        foreach (var a in all.Where(a => a.RegisterCheckedAt == null)) _abnsToCheck.Add(a.Abn);
        var stale = all.Where(a => a.RegisterCheckedAt < before)
            .OrderBy(a => a.RegisterCheckedAt)
            .Select(a => a.Abn)
            .Distinct()
            .Take(RegisterRefreshPerRun);
        foreach (var abn in stale) _abnsToCheck.Add(abn);
    }

    private sealed record RegisterRow(string Abn, string Name, string RegistrationDate);

    /// <summary>Every name under the queued ABNs, from the active register import, added to the ABN's first client.</summary>
    private async Task ReadRegisterAsync(CancellationToken ct)
    {
        if (_abnsToCheck.Count == 0) return;
        var queued = _abnsToCheck.ToList();
        _abnsToCheck.Clear();
        var hasImport = await db.BusinessNameImports.AnyAsync(i => i.IsActive, ct);
        if (!hasImport) return;

        foreach (var batch in queued.Chunk(500))
        {
            var json = JsonSerializer.Serialize(batch);
            // CAST keeps the comparison varchar like the column so each ABN is an index seek.
            var rows = await db.Database.SqlQuery<RegisterRow>($"""
                SELECT n.Abn, n.Name, n.RegistrationDate
                FROM RegisteredBusinessNames n WITH (NOLOCK)
                JOIN BusinessNameImports i ON i.Id = n.ImportId AND i.IsActive = 1
                WHERE n.Abn IN (SELECT CAST(value AS varchar(11)) FROM OPENJSON({json}))
                """).ToListAsync(ct);
            var byAbn = rows.GroupBy(r => r.Abn).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var abn in batch)
            {
                _result.AbnsChecked++;
                var clients = _byAbn.GetValueOrDefault(abn) ?? [];
                foreach (var a in clients.SelectMany(c => c.Abns).Where(a => a.Abn == abn)) a.RegisterCheckedAt = DateTime.UtcNow;
                if (clients.Count == 0) continue;

                // The first client on the ABN (the one any later "possible duplicate" points at) holds new names.
                var owner = clients.OrderBy(c => c.PossibleDuplicateOfId != null).ThenBy(c => c.CreatedAt).First();
                var onRegister = byAbn.GetValueOrDefault(abn) ?? [];
                foreach (var r in onRegister)
                {
                    var name = AddName(owner, abn, r.Name, "Register", r.RegistrationDate);
                    if (name == null) continue;
                    if (string.IsNullOrEmpty(name.RegistrationDate)) name.RegistrationDate = NullIfBlank(r.RegistrationDate, 10);
                    if (name.Status == ClientBusinessNameStatus.Cancelled) { name.Status = ClientBusinessNameStatus.Active; Touch(name); }
                }

                // A name gone from a non-empty register list has been cancelled. (An empty list
                // could be a cancelled ABN or a gap in the import, so nothing is assumed from it.)
                if (onRegister.Count == 0) continue;
                var current = onRegister.Select(r => NameKey(abn, r.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var held in clients.SelectMany(c => c.BusinessNames).Where(n => n.Abn == abn && n.Status == ClientBusinessNameStatus.Active))
                {
                    if (current.Contains(NameKey(abn, held.Name))) continue;
                    held.Status = ClientBusinessNameStatus.Cancelled;
                    Touch(held);
                    _result.NamesCancelled++;
                }
            }
        }
    }

    // ---- merging -----------------------------------------------------------------------

    public async Task MergeAsync(Guid sourceId, Guid targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) throw new InvalidOperationException("A client can't be merged into itself.");
        var source = await db.Clients.Include(c => c.Emails).Include(c => c.Abns).Include(c => c.BusinessNames).AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == sourceId, ct) ?? throw new InvalidOperationException("Client to merge not found.");
        var target = await db.Clients.Include(c => c.Emails).Include(c => c.Abns).AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == targetId, ct) ?? throw new InvalidOperationException("Client to merge into not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var e in source.Emails.ToList())
        {
            e.ClientId = target.Id;
            e.IsPrimary = e.IsPrimary && !target.Emails.Any(x => x.IsPrimary);
        }
        foreach (var a in source.Abns.ToList())
        {
            if (target.Abns.Any(x => x.Abn == a.Abn)) db.ClientAbns.Remove(a);
            else a.ClientId = target.Id;
        }
        foreach (var n in source.BusinessNames) n.ClientId = target.Id;

        FillProfile(target, new Identity
        {
            FullName = source.FullName, Mobile = source.MobileNumber, DateOfBirth = source.DateOfBirth,
            Address = source.Address, Suburb = source.Suburb, State = source.State, Postcode = source.Postcode,
            TfnEncrypted = source.TfnEncrypted, HasPurchased = source.HasPurchased, ReminderOptIn = source.ReminderOptIn,
            SeenAt = source.LastSeenAt,
        });
        if (!source.IsTest) target.IsTest = false;
        if (target.PossibleDuplicateOfId == source.Id) target.PossibleDuplicateOfId = null;
        if (!string.IsNullOrWhiteSpace(source.Notes))
            target.Notes = Truncate(string.Join("\n\n", new[] { target.Notes, source.Notes }.Where(s => !string.IsNullOrWhiteSpace(s))), 2000);
        await db.SaveChangesAsync(ct);

        await db.Leads.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        await db.OntraportSales.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        await db.RenewalRequests.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        await db.BulkRenewalUploads.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        await db.AsicKeyRequests.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        await db.OutboundMessages.Where(x => x.ClientId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, targetId), ct);
        // Anyone flagged as a possible duplicate of the source now points at the target (or nowhere, if it is the target).
        await db.Clients.Where(x => x.PossibleDuplicateOfId == sourceId && x.Id != targetId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PossibleDuplicateOfId, targetId), ct);

        db.Clients.Remove(source);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---- helpers -----------------------------------------------------------------------

    private static string NameKey(string abn, string? name) => $"{abn}|{Clean(name)}";

    private static void Touch(ClientBusinessName n) => n.UpdatedAt = DateTime.UtcNow;

    private static string Clean(string? value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();

    private static string? CleanEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        return e.Length > 3 && e.Contains('@') ? e : null;
    }

    private static string Digits(string? value) => AsicKeyRequestService.DigitsOnly(value);

    private static DateOnly? ParseDob(string? raw) =>
        AsicKeyRequestService.FormatDateOfBirth(raw) is { } s
        && DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        && d.Year > 1900
            ? d : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? NullIfBlank(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(Clean(value), max);
}
