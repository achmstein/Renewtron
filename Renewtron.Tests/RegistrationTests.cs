using Renewtron.Data;
using Renewtron.Services.Registration;
using Asic.Integrations.Portal;

namespace Renewtron.Tests;

/// <summary>
/// Reading ASIC Connect's "Apply for a Business Name" pages, from what ASIC showed on 11–12 Nov 2026
/// (ADAM ORTH PTY LTD, test names that were never paid for).
/// </summary>
public class RegistrationPageTests
{
    private static readonly AsicPortalClient.HolderAddress MartinPl = new("", "1", "Martin", "Pl", "Sydney", "NSW", "2000");

    [Fact]
    public void Name_check_says_available()
    {
        var (verdict, text) = AsicPortalClient.ReadNameVerdict(
            "<update id=\"name\"><![CDATA[<span>Check Name Availability</span> <span>HARBOUR LIGHT COIN TRADERS is currently available.</span> Select registration period and click next.]]></update>");
        Assert.Equal(BnNameVerdict.Available, verdict);
        Assert.Equal("HARBOUR LIGHT COIN TRADERS is currently available.", text);
    }

    [Fact]
    public void Name_check_says_manual_decision()
    {
        var (verdict, _) = AsicPortalClient.ReadNameVerdict(
            "Check Name Availability Name may not be available. COINPAYMENTS will be referred for a manual decision as it is unknown to ASIC and must be assessed. This name may be rejected. You may try another name.");
        Assert.Equal(BnNameVerdict.ManualDecision, verdict);
    }

    [Fact]
    public void Name_check_says_taken()
    {
        var (verdict, _) = AsicPortalClient.ReadNameVerdict(
            "The following error message(s) occurred: Name is not available. ORTH COIN PAYMENTS is identical or nearly identical to a name already registered, or currently under application or review. This name will be rejected. We suggest you try another name.");
        Assert.Equal(BnNameVerdict.NotAvailable, verdict);
    }

    [Fact]
    public void Reads_the_ABR_box_and_reference()
    {
        const string html = "<div>Business Name Availability Transaction reference number: 1-11AQR319 Details On The Australian Business Register (ABR) ABR Entity Name : ADAM ORTH PTY LTD ABN : 62677005793 Type : Incorporated Body Proposed Business Name Asterisk</div>";
        Assert.Equal("1-11AQR319", AsicPortalClient.ReadTransactionReference(html));
        Assert.Equal("ADAM ORTH PTY LTD", AsicPortalClient.ReadAbrField(html, "ABR Entity Name"));
        Assert.Equal("Incorporated Body", AsicPortalClient.ReadAbrField(html, "Type"));
    }

    [Fact]
    public void Reads_the_BPAY_details()
    {
        var (biller, crn) = AsicPortalClient.ReadBpayDetails(
            "We cannot process this transaction until you complete your payment. Biller Code: 17301 Customer Reference Number: 8597040845155 An electronic invoice");
        Assert.Equal("17301", biller);
        Assert.Equal("8597040845155", crn);
        Assert.Equal((null, null), AsicPortalClient.ReadBpayDetails("Biller Code: Customer Reference Number: Invoice"));
    }

    [Fact]
    public void Reads_the_card_payment_window()
    {
        var url = AsicPortalClient.ReadPaymentUrl(
            "<eval>window.open('https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=1-11ARCYUX&amp;SST=7c8f71f989e5435f8b990b45d51d6a36','pay')</eval>");
        Assert.Equal("https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=1-11ARCYUX&SST=7c8f71f989e5435f8b990b45d51d6a36", url);
    }

    [Fact]
    public void Knows_the_confirmation_page()
    {
        Assert.True(AsicPortalClient.IsRegistrationConfirmation(
            "Pay Later - You have successfully applied to register a business name Summary Your transaction was submitted on 12 Nov 2026"));
        Assert.False(AsicPortalClient.IsRegistrationConfirmation("Select Payment Preference"));
    }

    [Fact]
    public void Reads_the_fee_and_addresses_on_review()
    {
        const string review = "Registration Fee: $47.00 AUD 1 Year Business Hide Edit ACN/ARBN 677005793 Addresses Hide Edit Address type Address details This address is " +
                              "Address for service of documents 1 Martin Pl Sydney NSW 2000 Available to the public " +
                              "Principal place of business/practice Available to the public Email a@b.com Not available to the public";
        Assert.Equal(47.00m, AsicPortalClient.ReadAsicFee(review));
        Assert.True(AsicPortalClient.ReviewShowsAddress(review, "Address for service of documents", MartinPl));
        // The principal place was dropped (seen 12 Nov 2026): the run must not lodge.
        Assert.False(AsicPortalClient.ReviewShowsAddress(review, "Principal place of business/practice", MartinPl));
    }

    [Fact]
    public void Address_check_keeps_the_entered_address_over_a_shop_in_the_building()
    {
        var choices = new List<(string, string)>
        {
            ("v0", "Shop 1  1 Martin Place, SYDNEY  NSW  2000"),
            ("v1", "Level 7  1 Martin Place, SYDNEY  NSW  2000"),
            ("v130", "1 Martin Pl, Sydney NSW 2000   --  entered address"),
        };
        Assert.Equal("v130", AsicPortalClient.PickQasChoice(choices, MartinPl).Value);
    }

    [Fact]
    public void Address_check_takes_ASICs_spelling_of_the_same_place()
    {
        var choices = new List<(string, string)>
        {
            ("v0", "50 Bridge Street, SYDNEY  NSW  2000"),
            ("v1", "Level 1  50 Bridge Street, SYDNEY  NSW  2000"),
            ("v2", "50 Bridge St, Sydney NSW 2000   --  entered address"),
        };
        var bridge = new AsicPortalClient.HolderAddress("", "50", "Bridge", "St", "Sydney", "NSW", "2000");
        Assert.Equal("v0", AsicPortalClient.PickQasChoice(choices, bridge).Value);
    }

    [Fact]
    public void Reads_the_address_table_rows()
    {
        const string fetched = "<update id=\"tblAddr\"><![CDATA[<table><tr _afrRK=\"0\"><td>* Address for service of documents</td><td><table><tr><td>1 Martin Pl Sydney NSW 2000</td></tr></table></td><td><a id=\"tblAddr:0:addLink\">Add</a></td></tr>" +
                               "<tr _afrRK=\"1\"><td>* Principal place of business/practice</td><td></td><td><a id=\"tblAddr:1:addLink\">Add</a></td></tr>" +
                               "<tr _afrRK=\"2\"><td>* Email</td><td>a@b.com</td><td><a id=\"tblAddr:2:addLink\">Add</a></td></tr></table>]]></update>";
        var rows = AsicPortalClient.ParseAddressRows(fetched);
        Assert.Equal(["0", "1", "2"], rows.Select(r => r.Row));
        Assert.Contains("1 Martin Pl Sydney NSW 2000", rows[0].Text); // past the nested table's </tr>
        Assert.Contains("a@b.com", rows[2].Text);
    }

    [Fact]
    public void Reads_myTransactions()
    {
        const string html = """
            <table><tr role="row" _afrRK="0">
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c1"></td>
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c5"><span>1-11B0DLLP</span></td>
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c2"><span>Apply for Registration</span></td>
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c3"><span>Validated</span></td>
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c4"><span>declined</span></td>
            <td id="viewtransactions:j_id__ctru51:tbTrns:0:c6"><a id="viewtransactions:j_id__ctru51:tbTrns:0:cl1">Resume</a></td>
            </tr></table>
            """;
        var row = Assert.Single(AsicPortalClient.ParseTransactions(html));
        Assert.Equal(new AsicTransactionRow("1-11B0DLLP", "Apply for Registration", "Validated", "declined"), row);
        Assert.Equal("viewtransactions:j_id__ctru51:tbTrns:0", AsicPortalClient.ParseTransactionRowId(html, "1-11b0dllp"));
        Assert.Null(AsicPortalClient.ParseTransactionRowId(html, "1-99XXXXX"));
    }
}

/// <summary>The registration's own rules: ABN check, addresses, and what each ASIC outcome means.</summary>
public class RegistrationRuleTests
{
    [Theory]
    [InlineData("62677005793", true)]   // ADAM ORTH PTY LTD
    [InlineData("51824753556", true)]   // the ABR's example ABN
    [InlineData("51824753557", false)]
    [InlineData("1234", false)]
    public void ABN_checksum(string abn, bool ok) => Assert.Equal(ok, BusinessNameRegistrationService.AbnChecksumOk(abn));

    [Fact]
    public void Address_needs_street_suburb_state_and_postcode()
    {
        var ok = new RegistrationAddress { StreetNumber = "1", StreetName = "Martin", StreetType = "Pl", Suburb = "Sydney", State = "nsw", Postcode = "2000" };
        Assert.Null(BusinessNameRegistrationService.AddressProblem(ok, "address"));
        Assert.NotNull(BusinessNameRegistrationService.AddressProblem(new RegistrationAddress { StreetNumber = "1", StreetName = "Martin", StreetType = "Pl", Suburb = "Sydney", State = "XX", Postcode = "2000" }, "address"));
        Assert.NotNull(BusinessNameRegistrationService.AddressProblem(new RegistrationAddress { StreetNumber = "1", StreetName = "Martin", StreetType = "Pl", Suburb = "Sydney", State = "NSW", Postcode = "200" }, "address"));
    }

    private static BnRegistrationOutcome Outcome(BnRegistrationStage stage, BnNameVerdict verdict = BnNameVerdict.Available, string? step = null,
        string? problem = null, string? biller = null, string? crn = null) =>
        new(stage, "1-11ABCDEF", "ADAM ORTH PTY LTD", "Incorporated Body", verdict, null, null, biller, crn, problem, step, "");

    [Fact]
    public void Taken_name_is_refused_and_not_lodged()
    {
        var reg = new BusinessNameRegistration();
        BusinessNameRegistrationService.Apply(reg, Outcome(BnRegistrationStage.NotLodged, BnNameVerdict.NotAvailable, "Name", "ASIC says the name isn't available."));
        Assert.Equal(BusinessNameRegistrationStatus.Refused, reg.Status);
        Assert.Null(reg.LodgedAt);
    }

    [Fact]
    public void A_problem_filling_the_form_needs_a_look_not_a_refund()
    {
        var reg = new BusinessNameRegistration();
        BusinessNameRegistrationService.Apply(reg, Outcome(BnRegistrationStage.NotLodged, step: "Addresses", problem: "ASIC didn't keep the email."));
        Assert.Equal(BusinessNameRegistrationStatus.NeedsLook, reg.Status);
    }

    [Fact]
    public void Lodged_to_pay_by_BPAY_keeps_the_bill()
    {
        var reg = new BusinessNameRegistration();
        BusinessNameRegistrationService.Apply(reg, Outcome(BnRegistrationStage.AwaitingBpay, biller: "17301", crn: "8597040845155"));
        Assert.Equal(BusinessNameRegistrationStatus.AwaitingAsicPayment, reg.Status);
        Assert.Equal("17301", reg.BpayBillerCode);
        Assert.Equal("8597040845155", reg.BpayReference);
        Assert.NotNull(reg.LodgedAt);
    }

    [Fact]
    public void Unclear_after_lodging_is_still_marked_lodged()
    {
        var reg = new BusinessNameRegistration();
        BusinessNameRegistrationService.Apply(reg, Outcome(BnRegistrationStage.LodgedUnclear, problem: "Declare didn't open the Payment page."));
        Assert.Equal(BusinessNameRegistrationStatus.NeedsLook, reg.Status);
        Assert.NotNull(reg.LodgedAt); // so a retry can never lodge it twice
    }

    [Theory]
    [InlineData("paid", true)]
    [InlineData("Paid", true)]
    [InlineData("declined", false)]
    [InlineData("", false)]
    public void Payment_status_words(string status, bool paid) => Assert.Equal(paid, BusinessNameRegistrationService.IsPaid(status));

    [Theory]
    [InlineData("declined", true)]
    [InlineData("Payment Failed", true)]
    [InlineData("paid", false)]
    [InlineData("", false)]
    public void Declined_words(string status, bool declined) => Assert.Equal(declined, BusinessNameRegistrationService.IsDeclined(status));
}

/// <summary>Matching our business name to ASIC's list: loose, but never a guess between two names.</summary>
public class BusinessNameMatchTests
{
    private sealed record N(string Name);

    private static string? Find(string wanted, params string[] asic) =>
        Renewtron.Services.BusinessNameText.FindName(asic.Select(a => new N(a)).ToList(), n => n.Name, wanted)?.Name;

    [Fact] public void Spacing_and_ampersands() => Assert.Equal("L &P ANTIQUES AND COLLECTABLES", Find("L & P ANTIQUES AND COLLECTABLES", "OTHER NAME", "L &P ANTIQUES AND COLLECTABLES"));
    [Fact] public void ASIC_adds_a_suffix() => Assert.Equal("PartSculpt3d", Find("PartSculpt", "PartSculpt3d"));
    [Fact] public void We_add_pty_ltd() => Assert.Equal("DAY BREAK ELECTRICAL", Find("DAYBREAK ELECTRICAL Pty ltd", "DAY BREAK ELECTRICAL"));
    [Fact] public void Exact_wins_over_contained() => Assert.Equal("CYBER T", Find("Cyber T", "CYBER T", "CYBER T SECURITY"));
    [Fact] public void Two_possible_names_is_no_match() => Assert.Null(Find("Supersweet", "SUPERSWEET PASTRY", "SUPERSWEET CAKES"));
    [Fact] public void Different_name_is_no_match() => Assert.Null(Find("Wind-horse Cultural Enterprises", "Wind-horse and The Singing Gallery Cultural Enterprises", "SINGING GALLERY COFFEE"));
    [Fact] public void Short_names_need_exact() => Assert.Null(Find("ABC", "ABC PLUMBING"));
}
