using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// The ASIC key inbox's "Test" button: exercises the form's values without saving them. IMAP
/// login + folder + subject search, the Ontraport field id against the Contact object's
/// metadata, and the key regex against real letter wording. Each check reports independently
/// so a bad app password doesn't hide a bad field id.
/// </summary>
public sealed class AsicKeyInboxTester(ISettingsService settings, IAsicNotificationMailbox mailbox, IOntraportSalesService ontraport)
{
    /// <summary>Wording lifted from the ASIC letters seen in the inbox so far; the second has the
    /// key wrapped across a line, as long names cause.</summary>
    public static readonly (string Label, string Sample, string Expected)[] PatternSamples =
    [
        ("key letter", "Here is the ASIC Key for YUMOHZY TRITS: 1-67783322838.\nYou can use the ASIC Key to link", "1-67783322838"),
        ("key letter, wrapped", "Here is the ASIC Key for BRM BUILDING REPAIRS & MAINTENANCE: 1-\n79565779188.\nYou can use", "1-79565779188"),
        ("renewal notice", "Business name renewal notice for 'M.R.A. CONCRETING'\nAccount Number: 85 101951857\nASIC Key: 1-43002328008\nDue Date:", "1-43002328008"),
        ("renewal confirmation", "Registration of business name renewed for 'BECKS BAKES'\nThe ASIC key for this business name is 1-56809537698.\nThank you", "1-56809537698"),
    ];

    public async Task<object> TestAsync(AsicKeyInboxSettings body, CancellationToken ct)
    {
        var current = await settings.GetAsicKeyInboxSettingsAsync();
        SettingsRules.PrepareAsicKeyInbox(body, current);

        object mailboxCheck;
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
        {
            mailboxCheck = new { ok = false, error = "Enter the Gmail address and app password first." };
        }
        else
        {
            try
            {
                var probe = await mailbox.ProbeAsync(body, ct);
                mailboxCheck = new
                {
                    ok = true,
                    host = body.ImapHost,
                    username = body.Username,
                    folder = probe.Folder,
                    messagesInFolder = probe.MessagesInFolder,
                    matchingInLookback = probe.MatchingInLookback,
                    latestSubject = probe.LatestSubject,
                    latestReceivedAt = probe.LatestReceivedAt,
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                mailboxCheck = new { ok = false, error = ex.Message };
            }
        }

        object ontraportCheck;
        if (string.IsNullOrWhiteSpace(body.OntraportFieldId))
        {
            ontraportCheck = new { ok = false, error = "Ontraport field ID is empty — keys will be extracted but not written anywhere." };
        }
        else
        {
            try
            {
                var alias = await ontraport.GetContactFieldAliasAsync(body.OntraportFieldId.Trim());
                ontraportCheck = alias == null
                    ? new { ok = false, error = $"No field '{body.OntraportFieldId.Trim()}' exists on the Ontraport Contact object." }
                    : new { ok = true, fieldId = body.OntraportFieldId.Trim(), alias };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ontraportCheck = new { ok = false, error = ex.Message };
            }
        }

        return new { mailbox = mailboxCheck, ontraportField = ontraportCheck, keyPattern = CheckPattern(body.AsicKeyPattern) };
    }

    /// <summary>Does the pattern pull the right key out of every sample letter?</summary>
    public static object CheckPattern(string? pattern)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("pattern is empty");
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));

            var failures = new List<string>();
            foreach (var (label, sample, expected) in PatternSamples)
            {
                var match = regex.Match(sample);
                var extracted = match.Success
                    ? string.Concat((match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Where(c => !char.IsWhiteSpace(c)))
                    : null;
                if (extracted != expected)
                    failures.Add(extracted == null ? $"{label}: no match" : $"{label}: captured '{extracted}', expected '{expected}'");
            }
            return failures.Count == 0
                ? new { ok = true, sampleKey = PatternSamples[0].Expected, samples = PatternSamples.Length }
                : new { ok = false, error = "Pattern fails on real letter wording — " + string.Join("; ", failures) + "." };
        }
        catch (ArgumentException ex)
        {
            return new { ok = false, error = $"Invalid regex: {ex.Message}" };
        }
    }
}
