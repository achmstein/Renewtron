using Asic.Client;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;
using static Renewtron.Services.AsicPortal.AsicPortalText;

namespace Renewtron.Services.AsicPortal;

/// <summary>
/// "Link to our ASIC account" for one business name, in ASIC Connect over plain requests: link
/// it with the client's ASIC key and date of birth (as Registered Agent), change its business
/// email to ours, change an individual holder's email to ours (their address put back as it is),
/// then remove every representative except ours. Ported from David's asic-key-webhook, in the
/// order proven live on 7 Nov 2026. A step whose email is already ours is skipped, so a retry or a
/// name done before never lodges the same change twice.
/// </summary>
public interface IAsicPortalLinkService
{
    /// <summary>Records a run for the name and queues it. Null with a reason when it can't start.</summary>
    Task<(AsicPortalLink? Run, string? Problem)> QueueAsync(Guid clientBusinessNameId, AsicDeclareAs? declareAs, string? requestedBy);

    /// <summary>Queues a finished run again; it starts at its first step that isn't done.</summary>
    Task<string?> RetryAsync(Guid runId, string? requestedBy);

    /// <summary>Hangfire entry point: one run, one ASIC session, one at a time.</summary>
    [Queue(AsicPortalLinkService.Queue)]
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 0)]
    Task RunAsync(Guid runId);

    /// <summary>Logs in with the saved login and out again. Null when it worked, else what ASIC said.</summary>
    Task<string?> TestLoginAsync(CancellationToken ct = default);
}

public class AsicPortalLinkService(
    ApplicationDbContext db,
    IOptionsMonitor<AsicPortalSettings> settings,
    IOntraportSalesService ontraport,
    IBackgroundJobClient jobs,
    ILogger<AsicPortalLinkService> logger) : IAsicPortalLinkService
{
    public const string Queue = "asic-portal";

    /// <summary>Most representatives one run removes (one lodgement each).</summary>
    private const int MaxRemovals = 10;

    public async Task<(AsicPortalLink? Run, string? Problem)> QueueAsync(Guid clientBusinessNameId, AsicDeclareAs? declareAs, string? requestedBy)
    {
        var s = settings.CurrentValue;
        if (!s.Enabled) return (null, "Linking to our ASIC account is switched off in Settings.");
        if (!s.HasLogin) return (null, "No ASIC Connect login in Settings.");

        var name = await db.ClientBusinessNames.FirstOrDefaultAsync(n => n.Id == clientBusinessNameId);
        if (name is null) return (null, "Business name not found.");
        if (string.IsNullOrWhiteSpace(name.AsicKey)) return (null, "This business name has no ASIC key yet.");
        if (await db.AsicPortalLinks.AnyAsync(r => r.ClientBusinessNameId == name.Id
                && (r.Status == AsicPortalLinkStatus.Queued || r.Status == AsicPortalLinkStatus.Running)))
            return (null, "A link for this name is already queued or running.");

        var client = await db.ClientAbns.Where(a => a.Abn == name.Abn).Select(a => a.Client).FirstOrDefaultAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string dob, dobSource;
        if (client?.DateOfBirth is { } d) { dob = FormatDob(d); dobSource = "Client"; }
        else if (await db.OntraportSales.Where(x => x.Abn == name.Abn && x.DateOfBirth != null && x.DateOfBirth != "")
                     .OrderByDescending(x => x.SyncedAt).Select(x => x.DateOfBirth).FirstOrDefaultAsync() is { } saleDob)
        { dob = NormaliseDob(saleDob, today); dobSource = "Sale"; }
        else { dob = NormaliseDob(null, today); dobSource = "Today"; } // ASIC only checks it's a date

        var contactId = client is null ? null : await db.ClientEmails
            .Where(e => e.ClientId == client.Id && e.OntraportContactId != null)
            .OrderByDescending(e => e.IsPrimary).Select(e => e.OntraportContactId).FirstOrDefaultAsync();
        contactId ??= await db.OntraportSales.Where(x => x.Abn == name.Abn)
            .OrderByDescending(x => x.SyncedAt).Select(x => x.OntraportContactId).FirstOrDefaultAsync();

        var run = new AsicPortalLink
        {
            Id = Guid.NewGuid(),
            ClientId = client?.Id,
            ClientBusinessNameId = name.Id,
            BusinessName = name.Name,
            Abn = name.Abn,
            AsicKey = NormaliseAsicKey(name.AsicKey),
            DateOfBirth = dob,
            DateOfBirthSource = dobSource,
            OntraportContactId = string.IsNullOrWhiteSpace(contactId) ? null : contactId,
            DeclareAs = declareAs ?? s.DeclareAs,
            Status = AsicPortalLinkStatus.Queued,
            RequestedBy = requestedBy,
            CreatedAt = DateTime.UtcNow,
        };
        db.AsicPortalLinks.Add(run);
        await db.SaveChangesAsync();
        jobs.Enqueue<IAsicPortalLinkService>(x => x.RunAsync(run.Id));
        return (run, null);
    }

    public async Task<string?> RetryAsync(Guid runId, string? requestedBy)
    {
        var run = await db.AsicPortalLinks.FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null) return "Run not found.";
        if (run.Status is AsicPortalLinkStatus.Queued or AsicPortalLinkStatus.Running) return "It's already queued or running.";
        run.Status = AsicPortalLinkStatus.Queued;
        run.Error = null;
        run.FailedAtStep = null;
        run.RequestedBy = requestedBy ?? run.RequestedBy;
        await db.SaveChangesAsync();
        jobs.Enqueue<IAsicPortalLinkService>(x => x.RunAsync(run.Id));
        return null;
    }

    public async Task<string?> TestLoginAsync(CancellationToken ct = default)
    {
        var s = settings.CurrentValue;
        if (!s.HasLogin) return "No username or password saved.";
        using var client = new AsicPortalClient();
        var login = await client.LoginAsync(s.Username, s.Password, ct);
        if (login.Ok) await client.LogoutAsync(ct);
        return login.Ok ? null : login.Error;
    }

    public async Task RunAsync(Guid runId)
    {
        var run = await db.AsicPortalLinks.FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null || run.Status is not (AsicPortalLinkStatus.Queued or AsicPortalLinkStatus.Running)) return;

        var s = settings.CurrentValue;
        run.Status = AsicPortalLinkStatus.Running;
        run.StartedAt = DateTime.UtcNow;
        run.FinishedAt = null;
        run.AttemptCount++;
        run.Error = null;
        run.FailedAtStep = null;
        await db.SaveChangesAsync();

        using var client = new AsicPortalClient();
        try
        {
            if (!s.HasLogin) { Fail(run, "Login", "No ASIC Connect login in Settings."); return; }
            var login = await client.LoginAsync(s.Username, s.Password);
            if (!login.Ok) { Fail(run, "Login", login.Error ?? "ASIC Connect refused the login."); return; }

            var keep = s.KeepNames();
            var step = FirstStepToRun(run.LinkOutcome, run.EmailOutcome, run.HolderOutcome, run.RepsOutcome);
            if (step == Step.Link && !await LinkAsync(client, run)) return;
            await SaveAsync();
            if (step <= Step.Email) await EmailAsync(client, run, s);
            await SaveAsync();
            if (step <= Step.Holder) await HolderAsync(client, run, s);
            await SaveAsync();
            if (step <= Step.Representatives) await RepresentativesAsync(client, run, keep);

            run.Status = FirstStepToRun(run.LinkOutcome, run.EmailOutcome, run.HolderOutcome, run.RepsOutcome) == Step.Finished
                ? AsicPortalLinkStatus.Done
                : AsicPortalLinkStatus.NeedsLook;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ASIC portal link {RunId} for {BusinessName} failed", run.Id, run.BusinessName);
            Fail(run, run.FailedAtStep ?? "Run", ex.Message);
        }
        finally
        {
            try { await client.LogoutAsync(); } catch { /* best effort */ }
            run.FinishedAt = DateTime.UtcNow;
            await SaveAsync();
            await TellOntraportAsync(run, s);
            await SaveAsync();
            logger.LogInformation("ASIC portal link {RunId} for {BusinessName}: {Status} (link {Link}, email {Email}, holder {Holder}, reps {Reps})",
                run.Id, run.BusinessName, run.Status, run.LinkOutcome, run.EmailOutcome, run.HolderOutcome, run.RepsOutcome);
        }

        Task SaveAsync() => db.SaveChangesAsync();
    }

    /* ───────────── The steps ───────────── */

    private async Task<bool> LinkAsync(AsicPortalClient client, AsicPortalLink run)
    {
        run.FailedAtStep = "Link";
        var form = await client.OpenLinkBusinessAsync();
        if (form is null) { Fail(run, "Link", "Couldn't open \"Link a business\" (session ended)."); return false; }
        var answer = await client.SubmitLinkBusinessAsync(form, run.AsicKey, run.DateOfBirth);
        if (answer is null) { Fail(run, "Link", "The session ended while linking."); return false; }

        run.LinkOutcome = ClassifyLink(AsicPortalClient.PageText(answer.Html));
        run.FailedAtStep = null;
        switch (run.LinkOutcome)
        {
            case Outcomes.Success or Outcomes.AlreadyExists:
                if (await db.ClientBusinessNames.FirstOrDefaultAsync(n => n.Id == run.ClientBusinessNameId) is { } name)
                    name.PortalLinkedAt ??= DateTime.UtcNow;
                return true;
            case Outcomes.Failed:
                run.Status = AsicPortalLinkStatus.WrongKey;
                run.FailedAtStep = "Link";
                run.Error = "ASIC didn't accept the ASIC key and date of birth.";
                return false;
            default:
                run.Status = AsicPortalLinkStatus.NeedsLook;
                run.FailedAtStep = "Link";
                run.Error = "ASIC's answer to the link wasn't clear: check Lodgements & Notifications, then retry.";
                return false;
        }
    }

    private async Task EmailAsync(AsicPortalClient client, AsicPortalLink run, AsicPortalSettings s)
    {
        var open = await client.OpenNameActionAsync(run.BusinessName, AsicPortalClient.NameActions.BusinessAddress);
        if (open.Page is null) { StepProblem(run, "Business email", open.Problem); run.EmailOutcome = Outcomes.Failed; return; }

        // Already ours (a retry, or a name done before): leave the wizard without lodging anything.
        if (string.Equals(AsicPortalClient.BusinessEmailShown(open.Page.Html), s.BusinessEmail, StringComparison.OrdinalIgnoreCase))
        {
            await client.CancelWizardAsync(open.Page, open.Page.Html, "view");
            run.EmailOutcome = Outcomes.Success;
            run.EmailReference ??= "already ours";
            return;
        }

        var result = await client.ChangeBusinessEmailAsync(open.Page, s.BusinessEmail, run.DeclareAs);
        run.EmailOutcome = result.Lodged ? Outcomes.Success : Outcomes.Failed;
        if (result.Lodged) run.EmailReference = result.Reference;
        else StepProblem(run, "Business email", result.Problem);
    }

    /// <summary>
    /// Only an individual holder has an address and email to change: companies, partnerships and
    /// trusts have none on this page, so there's nothing to do (as in David's script). For an
    /// individual, their residential address is put back as it is, with our email.
    /// </summary>
    private async Task HolderAsync(AsicPortalClient client, AsicPortalLink run, AsicPortalSettings s)
    {
        var open = await client.OpenNameActionAsync(run.BusinessName, AsicPortalClient.NameActions.HolderDetails);
        if (open.Page is null) { StepProblem(run, "Holder email", open.Problem); run.HolderOutcome = Outcomes.Failed; return; }
        var type = AsicPortalClient.HolderType(open.Page.Html);
        run.HolderType = type;
        if (type is null)
        {
            run.HolderOutcome = Outcomes.Pending;
            StepProblem(run, "Holder email", "Couldn't read the holder's type: check the holder in ASIC Connect.");
            return;
        }
        if (!type.Equals("Individual", StringComparison.OrdinalIgnoreCase)) { run.HolderOutcome = Outcomes.NoAction; return; }

        var cells = AsicPortalClient.CellTexts(open.Page.Html);
        if (cells.Any(c => string.Equals(c.Trim(), s.BusinessEmail, StringComparison.OrdinalIgnoreCase)))
        {
            run.HolderOutcome = Outcomes.NoAction; // the holder's email is ours already
            return;
        }
        var addresses = cells.Where(LooksLikeHolderAddress).Distinct().ToList();
        if (addresses.Count == 0) { run.HolderOutcome = Outcomes.NoAddress; return; }
        if (addresses.Count > 1)
        {
            run.HolderOutcome = Outcomes.Pending;
            StepProblem(run, "Holder email", "More than one holder address: change the holders' email in ASIC Connect by hand.");
            return;
        }

        var parsed = ParseAusAddress(addresses[0]);
        if (!parsed.IsAustralian || !parsed.IsComplete || parsed.StreetTypeCode is not { } typeCode)
        {
            // Never guess at someone's address: a person does this one.
            run.HolderOutcome = Outcomes.Pending;
            StepProblem(run, "Holder email", $"Couldn't read the holder's address \"{addresses[0]}\" into ASIC's fields: change the holder's email in ASIC Connect by hand.");
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time")));
        var result = await client.ChangeHolderEmailAsync(open.Page,
            new AsicPortalClient.HolderAddress(parsed.Unit, parsed.StreetNumber, parsed.StreetName, typeCode, parsed.Suburb, parsed.State, parsed.Postcode),
            s.BusinessEmail, today, run.DeclareAs);
        run.HolderOutcome = result.Lodged ? Outcomes.Success : Outcomes.Failed;
        if (result.Lodged) run.HolderReference = result.Reference;
        else StepProblem(run, "Holder email", result.Problem);
    }

    private async Task RepresentativesAsync(AsicPortalClient client, AsicPortalLink run, IReadOnlyList<string> keep)
    {
        var removed = new List<string>();
        if (!string.IsNullOrEmpty(run.RepsRemoved)) removed.AddRange(run.RepsRemoved.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var references = new List<string>();
        if (!string.IsNullOrEmpty(run.RepsReferences)) references.AddRange(run.RepsReferences.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        for (var i = 0; i <= MaxRemovals; i++)
        {
            var open = await client.OpenNameActionAsync(run.BusinessName, AsicPortalClient.NameActions.Representatives);
            if (open.Page is null) { StepProblem(run, "Representatives", open.Problem); run.RepsOutcome = Outcomes.Failed; break; }

            var rows = AsicPortalClient.ReadRepresentatives(open.Page.Html);
            // Judge each row by its own name only: the row's words can carry the end of the row above.
            // A representative already removed (this run or an earlier one) can stay listed for a few
            // minutes while ASIC applies it: never lodge their removal twice.
            var target = rows.FirstOrDefault(r => !IsKeptRepresentative(RepresentativeName(r.Text), keep)
                && !removed.Contains(RepresentativeName(r.Text), StringComparer.OrdinalIgnoreCase));
            if (target is null)
            {
                run.RepsOutcome = Outcomes.NoAction;
                break;
            }
            if (i == MaxRemovals) { StepProblem(run, "Representatives", $"Still representatives left after {MaxRemovals} removals."); run.RepsOutcome = Outcomes.Failed; break; }

            var who = RepresentativeName(target.Text);
            var result = await client.RemoveRepresentativeAsync(open.Page, target, who, run.DeclareAs);
            if (!result.Lodged) { StepProblem(run, "Representatives", $"Removing \"{who}\": {result.Problem}"); run.RepsOutcome = Outcomes.Failed; break; }
            removed.Add(who);
            if (result.Reference is { } r) references.Add(r);
            run.RepsRemoved = string.Join('\n', removed);
            run.RepsReferences = string.Join('\n', references);
            await db.SaveChangesAsync();
        }
    }

    /* ───────────── Ontraport ───────────── */

    private async Task TellOntraportAsync(AsicPortalLink run, AsicPortalSettings s)
    {
        if (run.OntraportContactId is not { } contact) { run.OntraportResult = "No Ontraport contact."; return; }
        var value = run.LinkOutcome is Outcomes.Success or Outcomes.AlreadyExists ? "success"
            : run.Status == AsicPortalLinkStatus.WrongKey ? "fail" : null;
        if (value is null) return; // the link itself didn't get an answer: nothing to say yet
        try
        {
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.OntraportLinkFieldId))
                notes.Add(await ontraport.UpdateContactFieldsAsync(contact, new() { ["id"] = contact, [s.OntraportLinkFieldId] = value })
                    ? $"{s.OntraportLinkFieldId} = {value}" : $"{s.OntraportLinkFieldId} write refused");
            if (value == "fail" && !string.IsNullOrWhiteSpace(s.WrongKeyCampaignId))
                notes.Add(await ontraport.SubscribeToCampaignAsync(contact, s.WrongKeyCampaignId)
                    ? $"added to campaign {s.WrongKeyCampaignId}" : $"campaign {s.WrongKeyCampaignId} refused");
            var said = string.Join("; ", notes);
            run.OntraportResult = said.Length > 200 ? said[..200] : said;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ASIC portal link {RunId}: Ontraport update failed", run.Id);
            var said = "Ontraport update failed: " + ex.Message;
            run.OntraportResult = said.Length > 200 ? said[..200] : said;
        }
    }

    /* ───────────── Helpers ───────────── */

    private static void Fail(AsicPortalLink run, string step, string error)
    {
        run.Status = AsicPortalLinkStatus.Failed;
        run.FailedAtStep = step;
        run.Error = Cap(error);
    }

    private static string Cap(string s) => s.Length > 2000 ? s[..2000] : s;

    private static void StepProblem(AsicPortalLink run, string step, string? problem)
    {
        run.FailedAtStep ??= step;
        run.Error = string.IsNullOrEmpty(run.Error) ? $"{step}: {problem}" : $"{run.Error}\n{step}: {problem}";
    }
}
