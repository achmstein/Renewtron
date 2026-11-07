using System.Text.RegularExpressions;

namespace Renewtron.Services.AsicPortal;

/// <summary>
/// The text rules behind the ASIC Connect steps: tidying the key and date of birth, reading a
/// page's result, and splitting a displayed address back into ASIC's form fields. No browser
/// here, so it's all unit tested. Ported from David's asic-key-webhook scripts.
/// </summary>
public static partial class AsicPortalText
{
    /* ───────────── Inputs ───────────── */

    /// <summary>
    /// Any date of birth as dd/MM/yyyy. ASIC doesn't check the date against anything, so it only
    /// has to be well formed: odd separators and 2-digit years are fixed, and anything that still
    /// isn't a date becomes <paramref name="today"/>.
    /// </summary>
    public static string NormaliseDob(string? raw, DateOnly today)
    {
        var fallback = today.ToString("dd/MM/yyyy");
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return fallback;

        string? ExpandYear(string y) => y.Length switch
        {
            4 => y,
            2 => (int.Parse(y) <= today.Year % 100 ? "20" : "19") + y,
            _ => null,
        };
        string? Format(string d, string m, string? y)
        {
            if (y is null || !int.TryParse(d, out var dd) || !int.TryParse(m, out var mm) || !int.TryParse(y, out var yyyy)) return null;
            return dd is >= 1 and <= 31 && mm is >= 1 and <= 12 && yyyy >= 1900 && yyyy <= today.Year
                ? $"{dd:00}/{mm:00}/{yyyy}"
                : null;
        }

        var parts = NonDigits().Split(s).Where(p => p.Length > 0).ToArray();
        if (parts.Length == 3 && Format(parts[0], parts[1], ExpandYear(parts[2])) is { } separated) return separated;

        var digits = NonDigits().Replace(s, "");
        if (digits.Length == 8 && Format(digits[..2], digits[2..4], digits[4..]) is { } eight) return eight;
        if (digits.Length == 6 && Format(digits[..2], digits[2..4], ExpandYear(digits[4..])) is { } six) return six;
        return fallback;
    }

    /// <summary>A stored ISO date (yyyy-MM-dd) as dd/MM/yyyy.</summary>
    public static string FormatDob(DateOnly dob) => dob.ToString("dd/MM/yyyy");

    /// <summary>"1- 554 801.096 63" → "1-55480109663": spaces and dots out, the "1-" prefix in.</summary>
    public static string NormaliseAsicKey(string? raw)
    {
        var s = SpacesAndDots().Replace(raw ?? "", "");
        if (s.Length > 0 && !s.StartsWith("1-")) s = "1-" + s;
        return s;
    }

    /* ───────────── Reading results ───────────── */

    /// <summary>
    /// What the "Link a business" page said after submitting. "Existing association found" means
    /// the name is already on our account, which is as good as linked.
    /// </summary>
    public static string ClassifyLink(string pageText)
    {
        if (LinkAlreadyRe().IsMatch(pageText)) return Outcomes.AlreadyExists;
        var failed = LinkFailedRe().IsMatch(pageText);
        if (!failed && LinkSuccessRe().IsMatch(pageText)) return Outcomes.Success;
        return failed ? Outcomes.Failed : Outcomes.Unknown;
    }

    /// <summary>
    /// A wizard's real error banner. The "(Required)" and "Mandatory fields" labels are always on
    /// the page, so they don't count.
    /// </summary>
    public static bool HasWizardError(string pageText) => WizardErrorRe().IsMatch(pageText);

    /// <summary>The declaration page is still showing (its text is gone once ASIC accepts it).</summary>
    public static bool OnDeclarePage(string pageText) => DeclareRe().IsMatch(pageText);

    public static string? FindReference(string pageText) =>
        ReferenceRe().Match(pageText) is { Success: true } m ? m.Groups[1].Value : null;

    public static string? FindTransactionReference(string pageText) =>
        TransactionReferenceRe().Match(pageText) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Whitespace collapsed, for comparing names as ASIC shows them.</summary>
    public static string Squash(string? s) => Whitespace().Replace(s ?? "", " ").Trim();

    public static bool SameName(string? a, string? b) =>
        string.Equals(Squash(a), Squash(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A representative's name from its row's words, which end "… Change Address: Karen
    /// ParhaarIndividual" (ASIC runs the name into its type).
    /// </summary>
    public static string RepresentativeName(string rowText)
    {
        var s = Squash(rowText);
        var i = s.LastIndexOf(':');
        if (i >= 0) s = s[(i + 1)..].Trim();
        return RepTypeSuffix().Replace(s, "").Trim();
    }

    /// <summary>A representative row we keep: its text contains one of our names.</summary>
    public static bool IsKeptRepresentative(string rowText, IEnumerable<string> keepNames) =>
        keepNames.Any(n => n.Length > 0 && rowText.Contains(n, StringComparison.OrdinalIgnoreCase));

    /* ───────────── Addresses ───────────── */

    public static readonly string[] States = ["NSW", "VIC", "QLD", "WA", "SA", "TAS", "ACT", "NT"];

    public static readonly IReadOnlyDictionary<string, string> StateNames = new Dictionary<string, string>
    {
        ["NSW"] = "New South Wales", ["VIC"] = "Victoria", ["QLD"] = "Queensland", ["WA"] = "Western Australia",
        ["SA"] = "South Australia", ["TAS"] = "Tasmania", ["ACT"] = "Australian Capital Territory", ["NT"] = "Northern Territory",
    };

    /// <summary>
    /// A street-type word or abbreviation (lower case) → the code ASIC's "Street type" dropdown
    /// shows ("St", "Pde", "Cres"…). From ASIC's real option list.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StreetTypeCodes = BuildStreetTypes();

    /// <summary>
    /// A holder's address as ASIC displays it in one cell: starts with a unit word or a digit and
    /// ends "STATE, postcode Country". Length-capped so it can't match a whole actions cell.
    /// </summary>
    public static bool LooksLikeHolderAddress(string cellText) => HolderAddressRe().IsMatch(cellText);

    /// <summary>The JavaScript twin of <see cref="LooksLikeHolderAddress"/>, for scanning the page.</summary>
    public const string HolderAddressPattern =
        @"^(?:unit|units|u|level|lvl|suite|ste|shop|apartment|apt|flat|floor|fl|villa|room|rm|\d).{1,90}(?:NSW|VIC|QLD|WA|SA|TAS|ACT|NT),\s*\d{4}\s+[A-Za-z][A-Za-z ]*$";

    public sealed record AusAddress(
        string Unit, string StreetNumber, string StreetName, string StreetType,
        string Suburb, string State, string Postcode, string Country, string Raw)
    {
        /// <summary>Everything ASIC's form needs is there.</summary>
        public bool IsComplete =>
            StreetNumber.Length > 0 && StreetName.Length > 0 && StreetType.Length > 0 &&
            Suburb.Length > 0 && State.Length > 0 && Postcode.Length > 0;

        public bool IsAustralian => Country.Equals("Australia", StringComparison.OrdinalIgnoreCase);

        public string? StreetTypeCode =>
            StreetTypeCodes.TryGetValue(StreetType.ToLowerInvariant().Replace(".", "").Replace(",", ""), out var c) ? c : null;
    }

    /// <summary>
    /// Splits an address as ASIC displays it back into its form fields:
    /// "45 Armitage Dr Narre Warren South VIC, 3805 Australia",
    /// "Unit 98 15 Young Rd . Carlingford NSW, 2118 Australia",
    /// "5/12 Ocean Ave Bondi Beach NSW, 2026 Australia".
    /// The street type is the first known type word after the first word, so "Park Lane" is
    /// name Park, type Lane, and a suburb like "Ferny Grove" keeps its "Grove".
    /// </summary>
    public static AusAddress ParseAusAddress(string? raw)
    {
        var s = Whitespace().Replace(raw ?? "", " ").Trim();
        s = StrayDot().Replace(s, " ");
        s = TrailingDot().Replace(s, "").Trim();

        string country = "Australia", postcode = "", state = "", left = s;
        var comma = s.LastIndexOf(',');
        if (comma >= 0)
        {
            left = s[..comma].Trim();
            var right = s[(comma + 1)..].Trim();
            var pc = Postcode().Match(right);
            if (pc.Success) postcode = pc.Groups[1].Value;
            var c = Postcode().Replace(right, "", 1).Trim();
            if (c.Length > 0) country = c;
        }

        var tokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count > 0 && States.Contains(tokens[^1].ToUpperInvariant()))
        {
            state = tokens[^1].ToUpperInvariant();
            tokens.RemoveAt(tokens.Count - 1);
        }

        var unit = "";
        if (tokens.Count >= 2 && UnitWord().IsMatch(tokens[0]) && char.IsDigit(tokens[1][0]))
        {
            var u = tokens[1];
            if (u.Contains('/'))
            {
                var bits = u.Split('/', 2);
                unit = bits[0].Trim();
                tokens = [bits[1].Trim(), .. tokens.Skip(2)];
            }
            else
            {
                unit = u;
                tokens = tokens.Skip(2).ToList();
            }
        }

        var streetNumber = "";
        if (tokens.Count > 0 && tokens[0].Length > 0 && char.IsDigit(tokens[0][0]))
        {
            var first = tokens[0];
            tokens.RemoveAt(0);
            if (first.Contains('/'))
            {
                var bits = first.Split('/', 2);
                if (unit.Length == 0) unit = bits[0].Trim();
                streetNumber = bits[1].Trim();
            }
            else streetNumber = first;
        }

        string streetName, streetType = "", suburb = "";
        var typeIdx = tokens.Count > 1
            ? tokens.FindIndex(1, t => StreetTypeCodes.ContainsKey(t.ToLowerInvariant().Replace(".", "").Replace(",", "")))
            : -1;
        if (typeIdx >= 1)
        {
            streetName = string.Join(' ', tokens.Take(typeIdx));
            streetType = tokens[typeIdx].Replace(".", "").Replace(",", "");
            suburb = LeadingDots().Replace(string.Join(' ', tokens.Skip(typeIdx + 1)), "").Trim();
        }
        else streetName = string.Join(' ', tokens);

        return new AusAddress(unit, streetNumber, streetName, streetType, suburb, state, postcode, country, s);
    }

    public static string TodayDdMmYyyy(DateOnly today) => today.ToString("dd/MM/yyyy");

    /* ───────────── Retry ───────────── */

    /// <summary>
    /// The steps, in the order a run does them. The holder's email comes before removing
    /// representatives: on 7 Nov 2026, removing a name's only representative took away our
    /// account's "Change holder/address/representative" actions for that name (only "Other
    /// notifications" and "Renew" were left), so the holder change has to be done first.
    /// </summary>
    public enum Step { Link, Email, Holder, Representatives, Finished }

    /// <summary>
    /// Where a run starts (again): after the steps that are already done. A link that came back
    /// "already on our account" is done; a follow-up is done when it succeeded or there was
    /// nothing to do.
    /// </summary>
    public static Step FirstStepToRun(string? link, string? email, string? holder, string? reps)
    {
        if (link is not (Outcomes.Success or Outcomes.AlreadyExists)) return Step.Link;
        if (email != Outcomes.Success) return Step.Email;
        if (holder is not (Outcomes.Success or Outcomes.NoAction or Outcomes.NoAddress)) return Step.Holder;
        if (reps is not (Outcomes.NoAction)) return Step.Representatives;
        return Step.Finished;
    }

    public static class Outcomes
    {
        public const string Success = "success";
        public const string AlreadyExists = "already_exists";
        public const string Failed = "failed";
        public const string Unknown = "unknown";
        public const string NoAction = "no_action";
        public const string NoAddress = "no_address";
        public const string Pending = "pending";
        public const string Error = "error";
    }

    /* ───────────── Patterns ───────────── */

    [GeneratedRegex(@"\D+")] private static partial Regex NonDigits();
    [GeneratedRegex(@"\s*(Individual|Body Corporate|Company|Organisation|Partnership|Trust)$", RegexOptions.IgnoreCase)] private static partial Regex RepTypeSuffix();
    [GeneratedRegex(@"[\s.]")] private static partial Regex SpacesAndDots();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"\s+\.\s+")] private static partial Regex StrayDot();
    [GeneratedRegex(@"\s+\.$")] private static partial Regex TrailingDot();
    [GeneratedRegex(@"^[.\s]+")] private static partial Regex LeadingDots();
    [GeneratedRegex(@"\b(\d{4})\b")] private static partial Regex Postcode();
    [GeneratedRegex(@"^(unit|units|u|level|lvl|suite|ste|shop|apartment|apt|flat|floor|fl|villa|room|rm)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitWord();
    [GeneratedRegex(HolderAddressPattern, RegexOptions.IgnoreCase)] private static partial Regex HolderAddressRe();

    [GeneratedRegex(@"already exist|already linked|already a client|client already|existing (association|relationship)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkAlreadyRe();
    [GeneratedRegex(@"fail|error|unable|incorrect|does not match|not match|invalid", RegexOptions.IgnoreCase)]
    private static partial Regex LinkFailedRe();
    [GeneratedRegex(@"success|has been linked|linked|added to your|now linked", RegexOptions.IgnoreCase)]
    private static partial Regex LinkSuccessRe();
    [GeneratedRegex(@"messages for this page are listed below|following error message|error message\(s\)|must be (selected|completed)|you must (select|agree|confirm)", RegexOptions.IgnoreCase)]
    private static partial Regex WizardErrorRe();
    [GeneratedRegex(@"i declare that", RegexOptions.IgnoreCase)] private static partial Regex DeclareRe();
    [GeneratedRegex(@"reference number:?\s*([0-9A-Z-]+)", RegexOptions.IgnoreCase)] private static partial Regex ReferenceRe();
    [GeneratedRegex(@"Transaction reference number:\s*([0-9A-Z-]+)", RegexOptions.IgnoreCase)] private static partial Regex TransactionReferenceRe();

    private static Dictionary<string, string> BuildStreetTypes()
    {
        // word or abbreviation → ASIC code, as in David's STREET_TYPE_CODE table.
        const string table =
            "street:St st:St road:Rd rd:Rd drive:Dr dr:Dr drv:Dr avenue:Ave ave:Ave av:Ave parade:Pde pde:Pde " +
            "court:Ct ct:Ct crt:Ct place:Pl pl:Pl crescent:Cres cres:Cres cresc:Cres cr:Cres lane:Lane ln:Lane la:Lane " +
            "highway:Hwy hwy:Hwy terrace:Tce tce:Tce terr:Tce close:Cl cl:Cl boulevard:Bvd bvd:Bvd blvd:Bvd " +
            "grove:Gr gr:Gr circuit:Cct cct:Cct square:Sq sq:Sq parkway:Pkwy pkwy:Pkwy pwy:Pkwy way:Way wy:Way " +
            "esplanade:Esp esp:Esp circle:Cir cir:Cir rise:Rise rse:Rise view:View vw:View vista:Vsta vst:Vsta " +
            "mews:Mews mws:Mews quay:Qy qy:Qy quays:Qys ridge:Rdge rdg:Rdge gardens:Gdns gdns:Gdns gdn:Gdns garden:Gdns " +
            "green:Grn grn:Grn loop:Loop walk:Walk row:Row mall:Mall link:Link path:Path promenade:Prom prom:Prom " +
            "heights:Hts hts:Hts gully:Gly gly:Gly glen:Glen dale:Dale bend:Bend brae:Brae cove:Cove end:End " +
            "gap:Gap gate:Gte gte:Gte hill:Hill key:Key nook:Nook park:Park pass:Pass point:Pnt pnt:Pnt pt:Pnt " +
            "plaza:Plza plza:Plza quadrant:Qdrt quad:Quad ramp:Ramp reserve:Res res:Res ring:Ring run:Run spur:Spur " +
            "tarn:Tarn top:Top turn:Turn vale:Vale wharf:Whrf whrf:Whrf wynd:Wynd broadway:Bdwy bdwy:Bdwy " +
            "beach:Bch bch:Bch arcade:Arc arc:Arc alley:Ally ally:Ally common:Cmmn cmmn:Cmmn corner:Cnr cnr:Cnr " +
            "entrance:Ent ent:Ent freeway:Fwy fwy:Fwy frontage:Frtg frtg:Frtg junction:Jnc jnc:Jnc lookout:Lkt lkt:Lkt " +
            "outlook:Otlk bypass:Bypa causeway:Caus chase:Ch ch:Ch concourse:Cso cso:Cso crossing:Crsg crest:Crst " +
            "retreat:Rtt rtt:Rtt reach:Rch rch:Rch boardwalk:Bwlk fairway:Fawy flat:Flat line:Line meander:Mndr mndr:Mndr " +
            "motorway:Mwy mwy:Mwy range:Rnge rnge:Rnge river:Rvr slope:Slpe steps:Stps strip:Strp track:Trk trk:Trk trail:Trl trl:Trl";
        return table.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(':'))
            .ToDictionary(p => p[0], p => p[1]);
    }
}
