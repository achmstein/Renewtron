using Asic.Client;
using Renewtron.Services.AsicPortal;
using Xunit.Abstractions;
using static Renewtron.Services.AsicPortal.AsicPortalText;

namespace Renewtron.Tests;

/// <summary>The text rules behind "link to our ASIC account", from David's scripts.</summary>
public class AsicPortalTextTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Theory]
    [InlineData("15/03/1980", "15/03/1980")]
    [InlineData("5-3-1980", "05/03/1980")]
    [InlineData("05.03.80", "05/03/1980")]
    [InlineData("1 3 05", "01/03/2005")]
    [InlineData("15031980", "15/03/1980")]
    [InlineData("150380", "15/03/1980")]
    [InlineData("31/13/1980", "06/11/2026")]   // no 13th month: today
    [InlineData("", "06/11/2026")]
    [InlineData(null, "06/11/2026")]
    [InlineData("not a date", "06/11/2026")]
    public void Date_of_birth_comes_out_as_dd_mm_yyyy(string? raw, string expected) =>
        Assert.Equal(expected, NormaliseDob(raw, Today));

    [Theory]
    [InlineData("1- 554 801.096 63", "1-55480109663")]
    [InlineData("55480109663", "1-55480109663")]
    [InlineData("1-55480109663", "1-55480109663")]
    [InlineData("", "")]
    public void Asic_key_is_tidied(string raw, string expected) => Assert.Equal(expected, NormaliseAsicKey(raw));

    [Theory]
    [InlineData("Existing association found for this business.", Outcomes.AlreadyExists)]
    [InlineData("The business has been linked to your account.", Outcomes.Success)]
    [InlineData("The ASIC key does not match our records.", Outcomes.Failed)]
    [InlineData("Please wait", Outcomes.Unknown)]
    public void Link_result_is_read_from_the_page(string text, string expected) => Assert.Equal(expected, ClassifyLink(text));

    [Fact]
    public void Declare_as_picks_the_authority_radio()
    {
        Assert.Equal("authority4", Asic.Client.AsicDeclare.AuthorityValue(Asic.Client.AsicDeclareAs.OnBehalfOfHolder));
        Assert.Equal("authority3", Asic.Client.AsicDeclare.AuthorityValue(Asic.Client.AsicDeclareAs.Holder));
        var fields = Asic.Client.AsicDeclare.Fields(Asic.Client.AsicDeclareAs.Holder);
        Assert.Equal("t", fields["declaration1"]);
        Assert.Equal("t", fields["declaration2"]);
        Assert.Equal("authority3", fields["authorityOption"]);
    }

    [Fact]
    public void Required_labels_are_not_wizard_errors()
    {
        Assert.False(HasWizardError("Email address (Required) Mandatory fields are marked"));
        Assert.True(HasWizardError("The following error message(s) need your attention"));
        Assert.True(HasWizardError("A declaration must be selected"));
    }

    [Fact]
    public void Plain_address_splits_into_fields()
    {
        var a = ParseAusAddress("45 Armitage Dr Narre Warren South VIC, 3805 Australia");
        Assert.Equal(("", "45", "Armitage", "Dr", "Narre Warren South", "VIC", "3805", "Australia"),
            (a.Unit, a.StreetNumber, a.StreetName, a.StreetType, a.Suburb, a.State, a.Postcode, a.Country));
        Assert.True(a.IsComplete);
        Assert.Equal("Dr", a.StreetTypeCode);
    }

    [Fact]
    public void Unit_word_and_stray_dot()
    {
        var a = ParseAusAddress("Unit 98 15 Young Rd . Carlingford NSW, 2118 Australia");
        Assert.Equal(("98", "15", "Young", "Rd", "Carlingford", "NSW", "2118"),
            (a.Unit, a.StreetNumber, a.StreetName, a.StreetType, a.Suburb, a.State, a.Postcode));
    }

    [Fact]
    public void Unit_slash_number()
    {
        var a = ParseAusAddress("5/12 Ocean Ave Bondi Beach NSW, 2026 Australia");
        Assert.Equal(("5", "12", "Ocean", "Ave", "Bondi Beach"), (a.Unit, a.StreetNumber, a.StreetName, a.StreetType, a.Suburb));
    }

    [Fact]
    public void Street_name_that_is_a_type_word_keeps_the_next_word_as_type()
    {
        // "Park Lane": name Park, type Lane; the suburb's own "Grove" is left in the suburb.
        var a = ParseAusAddress("3 Park Lane Ferny Grove QLD, 4055 Australia");
        Assert.Equal(("Park", "Lane", "Ferny Grove"), (a.StreetName, a.StreetType, a.Suburb));
        Assert.Equal("Lane", a.StreetTypeCode);
    }

    [Fact]
    public void Overseas_and_incomplete_addresses_are_flagged()
    {
        Assert.False(ParseAusAddress("10 Queen St Auckland, 1010 New Zealand").IsAustralian);
        Assert.False(ParseAusAddress("PO Box 12 Sydney NSW, 2000 Australia").IsComplete);
    }

    [Theory]
    [InlineData("45 Armitage Dr Narre Warren South VIC, 3805 Australia", true)]
    [InlineData("Unit 98 15 Young Rd Carlingford NSW, 2118 Australia", true)]
    [InlineData("Change address Change name", false)]
    public void Holder_address_cell_is_recognised(string cell, bool expected) => Assert.Equal(expected, LooksLikeHolderAddress(cell));

    [Fact]
    public void Kept_representatives_match_anywhere_in_the_row()
    {
        string[] keep = ["David Orth", "Business David Names Individual"];
        Assert.True(IsKeptRepresentative("Mr DAVID ORTH Remove", keep));
        Assert.False(IsKeptRepresentative("Jane Citizen Remove", keep));
    }

    [Theory]
    [InlineData(null, null, null, null, Step.Link)]
    [InlineData(Outcomes.Failed, null, null, null, Step.Link)]
    [InlineData(Outcomes.AlreadyExists, null, null, null, Step.Email)]
    [InlineData(Outcomes.Success, Outcomes.Success, null, null, Step.Holder)]
    [InlineData(Outcomes.Success, Outcomes.Success, Outcomes.Unknown, Outcomes.NoAction, Step.Holder)]
    [InlineData(Outcomes.Success, Outcomes.Success, Outcomes.Success, Outcomes.Failed, Step.Representatives)]
    [InlineData(Outcomes.Success, Outcomes.Success, Outcomes.NoAddress, Outcomes.NoAction, Step.Finished)]
    public void Retry_starts_at_the_first_step_not_done(string? link, string? email, string? holder, string? reps, Step expected) =>
        Assert.Equal(expected, FirstStepToRun(link, email, holder, reps));

    [Fact]
    public void Holder_comes_before_representatives() =>
        Assert.True(Step.Holder < Step.Representatives);
}

/// <summary>Reading ASIC Connect's login (Oracle Access Manager) and ADF pages.</summary>
public class AsicPortalClientParsingTests
{
    // The shape of auth.asic.gov.au/logon/login.jsp as served on 6 Nov 2026.
    private const string LoginPage = """
        <form id="search" action="#"><input type="text" name="keywords" id="keywords"/></form>
        <form id="loginForm" action="/oam/server/auth_cred_submit" method="post">
          <input id="username" name="userid" type="text" maxlength="80" size="25" value="" tabindex="1"/>
          <input id="password" type="password" maxlength="255" size="25" name="password" autocomplete="off" tabindex="2"/>
          <input type="hidden" name="request_id" value="3521741249688041630" />
          <input type="image" value="submitname" src="/logon/images/login-butt-static.png" alt="Log in" name="image" class="btnLogin" tabindex="3"/>
        </form>
        """;

    [Fact]
    public void Login_form_and_its_request_id_are_found()
    {
        var form = AsicPortalClient.ParseLoginForm(LoginPage);
        Assert.NotNull(form);
        Assert.Equal("/oam/server/auth_cred_submit", form!.Action);
        Assert.Equal("3521741249688041630", form.Hidden["request_id"]);
    }

    [Fact]
    public void Loopback_values_are_read()
    {
        const string html = """
             AdfLoopbackUtils.runLoopback(
             12,
             '_afrLoop',
             '1139053680756408',
             '_afrWindowMode',
             'Adf-Window-Id',
             '_afrPage',
             '',
             'qqc9xh6r9',
             ';jsessionid=abc!123',
             '!',
            """;
        var loop = AsicPortalClient.ParseLoopback(html);
        Assert.Equal(("1139053680756408", "qqc9xh6r9", ";jsessionid=abc!123"), (loop!.Loop, loop.WindowId, loop.SessionSuffix));
        Assert.Null(AsicPortalClient.ParseLoopback("<html>no loop here</html>"));
    }

    [Theory]
    [InlineData("https://auth.asic.gov.au:443/logon/login.jsp?bmctx=1&p_error_code=OAM-2", "username or password is wrong")]
    [InlineData("https://auth.asic.gov.au:443/logon/login.jsp?p_error_code=OAM-5", "locked")]
    public void Login_errors_are_put_in_words(string url, string expected) =>
        Assert.Contains(expected, AsicPortalClient.LoginError(url, ""));

    [Fact]
    public void Auth_host_is_recognised()
    {
        Assert.True(AsicPortalClient.IsAuthUrl("https://auth.asic.gov.au:443/logon/login.jsp?x=1"));
        Assert.False(AsicPortalClient.IsAuthUrl("https://asicconnect.asic.gov.au/emeraldCONNECT/faces/HomePage"));
    }
}

/// <summary>
/// Against the real ASIC Connect. Skipped unless ASIC_PORTAL_LIVE=1. With ASIC_PORTAL_USERNAME and
/// ASIC_PORTAL_PASSWORD also set, it logs in and out with that account; without them it only
/// checks that the protected home page leads to a login form we can read.
/// </summary>
public class AsicPortalLiveTests(ITestOutputHelper output)
{
    private static bool Live => Environment.GetEnvironmentVariable("ASIC_PORTAL_LIVE") == "1";

    [Fact]
    public async Task Home_page_leads_to_a_login_form_we_can_read()
    {
        if (!Live) return;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true });
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/125.0.0.0 Safari/537.36");
        using var response = await http.GetAsync(AsicPortalClient.BaseUrl + AsicPortalClient.HomePath);
        var url = response.RequestMessage!.RequestUri!.ToString();
        var form = AsicPortalClient.ParseLoginForm(await response.Content.ReadAsStringAsync());
        output.WriteLine($"landed on {url[..Math.Min(80, url.Length)]}…; form posts to {form?.Action}; hidden: {string.Join(", ", form?.Hidden.Keys ?? [])}");
        Assert.True(AsicPortalClient.IsAuthUrl(url));
        Assert.NotNull(form);
        Assert.True(form!.Hidden.ContainsKey("request_id"));
    }

    [Fact]
    public async Task Log_in_and_out()
    {
        var user = Environment.GetEnvironmentVariable("ASIC_PORTAL_USERNAME");
        var pass = Environment.GetEnvironmentVariable("ASIC_PORTAL_PASSWORD");
        if (!Live || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return;

        using var client = new AsicPortalClient();
        var login = await client.LoginAsync(user, pass);
        output.WriteLine(login.Ok ? $"logged in; home {login.Home!.Url[..60]}…, window {login.Home.WindowId}" : $"login failed: {login.Error}");
        Assert.True(login.Ok, login.Error);

        var again = await client.OpenAsync(AsicPortalClient.HomePath);
        output.WriteLine(again is null
            ? "second visit: sent to the login page"
            : $"second visit: {again.Url[..Math.Min(120, again.Url.Length)]} ({again.Html.Length} chars), logged-in home: {again.IsLoggedInHome}, window {again.WindowId}");
        if (Environment.GetEnvironmentVariable("ASIC_PORTAL_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllText(Path.Combine(dump, "home1.html"), login.Home.Html);
            if (again is not null) File.WriteAllText(Path.Combine(dump, "home2.html"), again.Html);
        }
        if (again is { IsLoggedInHome: false })
            output.WriteLine("page text: " + System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(again.Html, "<script.*?</script>|<style.*?</style>", "", System.Text.RegularExpressions.RegexOptions.Singleline),
                @"<[^>]+>|\s+", " ")[..Math.Min(600, again.Html.Length)]);
        Assert.True(again?.IsLoggedInHome, "the home page didn't show the logged-in actions on a second visit");
        await client.LogoutAsync();
    }

    /// <summary>Read-only: opens the link form and the Lodgements list, submits nothing.</summary>
    [Fact]
    public async Task Opens_the_link_form_and_the_lodgements_list()
    {
        var user = Environment.GetEnvironmentVariable("ASIC_PORTAL_USERNAME");
        var pass = Environment.GetEnvironmentVariable("ASIC_PORTAL_PASSWORD");
        if (!Live || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return;

        using var client = new AsicPortalClient();
        Assert.True((await client.LoginAsync(user, pass)).Ok);
        try
        {
            var link = await client.OpenLinkBusinessAsync();
            Assert.NotNull(link);
            var form = AsicPortalClient.ReadForm(link!.Html, AsicPortalClient.LinkForm.Submit);
            output.WriteLine($"link page {link.Url[..60]}…, form {form?.Id}, {form?.Fields.Count} fields");
            foreach (var field in new[] { AsicPortalClient.LinkForm.Kind, AsicPortalClient.LinkForm.DateOfBirth, AsicPortalClient.LinkForm.Key, AsicPortalClient.LinkForm.Role })
                Assert.True(form!.Fields.ContainsKey(field), $"link form has no {field}");
            Assert.Contains(AsicPortalClient.LinkForm.HasKeyYes, link.Html);

            var lodgements = await client.OpenLodgementsAsync();
            Assert.NotNull(lodgements);
            var rows = System.Text.RegularExpressions.Regex.Matches(lodgements!.Html, @"tableBN:(\d+):maitainBNRadio::content")
                .Select(m => m.Groups[1].Value).Distinct().Count();
            output.WriteLine($"lodgements page: {rows} business names");
            Assert.True(rows > 0);
        }
        finally
        {
            await client.LogoutAsync();
        }
    }

    /// <summary>
    /// REAL ACTION: links one business name to our ASIC account as Registered Agent. Runs only when
    /// ASIC_PORTAL_LINK_NAME, ASIC_PORTAL_LINK_KEY and ASIC_PORTAL_LINK_DOB (dd/MM/yyyy) are set.
    /// </summary>
    [Fact]
    public async Task Links_one_business_name()
    {
        var user = Environment.GetEnvironmentVariable("ASIC_PORTAL_USERNAME");
        var pass = Environment.GetEnvironmentVariable("ASIC_PORTAL_PASSWORD");
        var name = Environment.GetEnvironmentVariable("ASIC_PORTAL_LINK_NAME");
        var key = Environment.GetEnvironmentVariable("ASIC_PORTAL_LINK_KEY");
        var dob = Environment.GetEnvironmentVariable("ASIC_PORTAL_LINK_DOB");
        if (!Live || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(dob)) return;
        var dump = Environment.GetEnvironmentVariable("ASIC_PORTAL_DUMP");

        using var client = new AsicPortalClient();
        var login = await client.LoginAsync(user, pass);
        Assert.True(login.Ok, login.Error);
        try
        {
            var before = await client.OpenLodgementsAsync();
            output.WriteLine($"on our account before: {before!.Html.Contains(name, StringComparison.OrdinalIgnoreCase)}");

            var link = await client.OpenLinkBusinessAsync();
            Assert.NotNull(link);
            var answer = await client.SubmitLinkBusinessAsync(link!, NormaliseAsicKey(key), dob);
            Assert.NotNull(answer);
            if (dump is { Length: > 0 }) File.WriteAllText(Path.Combine(dump, "link-answer.html"), answer!.Html);
            var text = Squash(System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(answer!.Html, "<script.*?</script>|<style.*?</style>", " ", System.Text.RegularExpressions.RegexOptions.Singleline), "<[^>]+>", " "));
            var outcome = ClassifyLink(text);
            output.WriteLine($"answer page {answer.Url[..Math.Min(80, answer.Url.Length)]}, outcome: {outcome}");
            var i = text.IndexOf("Link business", StringComparison.OrdinalIgnoreCase);
            output.WriteLine("text: " + text.Substring(Math.Max(0, i), Math.Min(600, text.Length - Math.Max(0, i))));
            Assert.NotEqual(Outcomes.Unknown, outcome);

            var after = await client.OpenLodgementsAsync();
            output.WriteLine($"on our account after: {after!.Html.Contains(name, StringComparison.OrdinalIgnoreCase)}");
        }
        finally
        {
            await client.LogoutAsync();
        }
    }
}

/// <summary>Reading the ASIC Connect pages behind the follow-up steps (email, holder, representatives).</summary>
public class AsicPortalStepPageTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Remove Representative Change Address Change Address: Karen ParhaarIndividual", "Karen Parhaar")]
    [InlineData("Update Name: ALA PartnersIndividual", "ALA Partners")]
    [InlineData("Change Address: sdfgsdfg David sdfgsdfgIndividual", "sdfgsdfg David sdfgsdfg")]
    [InlineData("Change Address: SOME CO PTY LTDBody Corporate", "SOME CO PTY LTD")]
    public void Representative_name_comes_from_the_end_of_its_row(string row, string expected) =>
        Assert.Equal(expected, RepresentativeName(row));

    [Fact]
    public void Our_own_representative_is_kept_by_default()
    {
        var keep = new Renewtron.Settings.AsicPortalSettings().KeepNames();
        Assert.True(IsKeptRepresentative(RepresentativeName("Change Address: sdfgsdfg David sdfgsdfgIndividual"), keep));
        Assert.False(IsKeptRepresentative(RepresentativeName("Change Address: Karen ParhaarIndividual"), keep));
    }

    private const string RepsDeclare = """
        <update id="tmp:reg"><![CDATA[<div>
        <input id="tmp:reg:2:sbc1::content" name="tmp:reg:2:sbc1" class="x" type="checkbox" value="t" onclick="this.focus()"><label>This transaction is submitted for lodgement under the protocol.</label>
        <input id="tmp:reg:2:sbc2::content" name="tmp:reg:2:sbc2" class="x" type="checkbox" value="t" onclick="this.focus()"><label>To the best of my knowledge the information is complete.</label>
        <table id="tmp:reg:2:sbc3"><tr><td><input id="tmp:reg:2:sbc3::content" name="authority" class="x" role="radio" type="radio" value="tmp:reg:2:sbc3" onclick="this.focus()"><label>Business owner declaration</label></td></tr></table>
        <p>I am the business name holder or one of the business name holders and am authorised to submit this transaction for lodgement. OR</p>
        <table id="tmp:reg:2:sbc4"><tr><td><input id="tmp:reg:2:sbc4::content" name="authority" class="x" role="radio" type="radio" value="tmp:reg:2:sbc4" onclick="this.focus()"><label>Representative declaration</label></td></tr></table>
        <p>I am lodging this transaction on behalf of the business name holder or holders and am authorised to submit it.</p>
        <button id="tmp:reg:2:cbDeclaration" class="b">Next</button>
        </div>]]></update>
        """;

    [Theory]
    [InlineData(AsicDeclareAs.Holder, "tmp:reg:2:sbc3")]
    [InlineData(AsicDeclareAs.OnBehalfOfHolder, "tmp:reg:2:sbc4")]
    public void Declaration_radio_is_chosen_by_its_words(AsicDeclareAs who, string radio)
    {
        var choice = AsicPortalClient.ChooseDeclaration(RepsDeclare, who);
        Assert.NotNull(choice);
        Assert.Equal(radio, choice!.RadioId);
        Assert.Equal(["tmp:reg:2:sbc1", "tmp:reg:2:sbc2"], choice.Boxes);
        Assert.Equal("t", choice.Fields["tmp:reg:2:sbc1"]);
        Assert.Equal(radio, choice.Fields["authority"]);
        Assert.Equal("tmp:reg:2:cbDeclaration", AsicPortalClient.FindButton(RepsDeclare, "Next"));
        Assert.Equal("tmp", AsicPortalClient.RegionOf("tmp:reg:2:cbDeclaration"));
    }

    [Fact]
    public void A_declare_page_without_the_two_declarations_is_refused() =>
        Assert.Null(AsicPortalClient.ChooseDeclaration("<input name=\"a\" type=\"checkbox\" value=\"t\">", AsicDeclareAs.Holder));

    [Theory]
    [InlineData("<![CDATA[ Confirmation Transaction reference number: 1-11987U4D You have successfully submitted this transaction. ]]>", true, "1-11987U4D")]
    [InlineData("<![CDATA[ Confirmation Transaction reference number: 1-1197WNVJ You have successfully submitted your request. ]]>", true, "1-1197WNVJ")]
    [InlineData("<![CDATA[ Declare Transaction reference number: 1-1197WNVJ I declare that ]]>", false, null)]
    public void Lodgement_is_confirmed_by_asics_words(string response, bool lodged, string? reference)
    {
        var r = AsicPortalClient.ReadConfirmation(response);
        Assert.Equal(lodged, r.Lodged);
        Assert.Equal(reference, r.Reference);
    }

    [Theory]
    [InlineData("Name of holder : SRT TRANSPORT PTY. LTD. Holder type : Body Corporate Business name holders Below is a list", "Body Corporate")]
    [InlineData("Holder type : Individual Business name holders Below", "Individual")]
    public void Holder_type_is_read(string text, string expected) =>
        Assert.Equal(expected, AsicPortalClient.HolderType($"<div>{text}</div>"));

    [Fact]
    public void Row_actions_are_read_from_the_partial_answer()
    {
        const string answer = """<select id="x:tableBN:3:maintainDropdown::content" name="x"><option value="" selected>Select Option</option><option value="1">Change business address</option><option value="2">Change holder details</option><option value="3">Change representative details</option></select>""";
        var options = AsicPortalClient.ParseOptions(answer, "x:tableBN:3:maintainDropdown");
        Assert.Equal("2", options[AsicPortalClient.NameActions.HolderDetails]);
        Assert.Equal(4, options.Count);
    }

    [Fact]
    public void Name_row_is_found_by_its_name()
    {
        const string html = """
            <input id="biZ:tableBN:0:maitainBNRadio::content" type="radio"><td>OTHER NAME</td>
            <input id="biZ:tableBN:1:maitainBNRadio::content" type="radio"><td>Steve   Rinaldi Trucking</td>
            """;
        Assert.Equal("biZ:tableBN:1:maitainBNRadio", AsicPortalClient.FindNameRowRadio(html, "STEVE RINALDI TRUCKING"));
        Assert.Null(AsicPortalClient.FindNameRowRadio(html, "NOT HERE"));
    }

    /// <summary>
    /// The same rules against real pages saved from ASIC Connect (they hold client data, so they
    /// stay out of the repo). Runs when ASIC_PORTAL_PAGES points at the folder.
    /// </summary>
    [Fact]
    public void Real_saved_pages()
    {
        var dir = Environment.GetEnvironmentVariable("ASIC_PORTAL_PAGES");
        if (string.IsNullOrEmpty(dir)) return;
        string Read(string f) => File.ReadAllText(Path.Combine(dir, f));

        var declare = AsicPortalClient.ChooseDeclaration(Read("rp4-r2.xml"), AsicDeclareAs.Holder);
        Assert.Equal("tmp:reg:2:sbc3", declare?.RadioId);
        Assert.Equal("tmp:reg:2:sbc4", AsicPortalClient.ChooseDeclaration(Read("rp4-r2.xml"), AsicDeclareAs.OnBehalfOfHolder)?.RadioId);
        Assert.Equal("tmp:reg:2:cbDeclaration", AsicPortalClient.FindButton(Read("rp4-r2.xml"), "Next"));

        Assert.True(AsicPortalClient.ReadConfirmation(Read("rp4-r3.xml")).Lodged);
        Assert.True(AsicPortalClient.ReadConfirmation(Read("e-r6.xml")).Lodged);
        Assert.Equal("Body Corporate", AsicPortalClient.HolderType(Read("holder3.html")));
        Assert.Equal("2", AsicPortalClient.FindEmailRow(Read("email-live.html")));

        var lodgements = Read("lodg.html");
        var row = AsicPortalClient.FindNameRowRadio(lodgements, "NAKED SKIN AESTHETICA");
        output.WriteLine($"row radio: {row}");
        Assert.NotNull(row);
        Assert.EndsWith(":maitainBNRadio", row);

        var reps = AsicPortalClient.ReadRepresentatives(Read("reps-live.html"));
        foreach (var r in reps) output.WriteLine($"rep {r.Index}: {RepresentativeName(r.Text)} ({r.RemoveId})");
        Assert.NotEmpty(reps);
        Assert.Contains(reps, r => RepresentativeName(r.Text) == "sdfgsdfg David sdfgsdfg");
    }
}
