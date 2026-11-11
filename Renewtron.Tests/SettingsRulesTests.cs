using Asic.Integrations.Registry;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Tests;

public class SettingsRulesTests
{
    [Fact]
    public void Masking_keeps_the_last_four_and_a_round_trip_keeps_the_stored_secret()
    {
        Assert.Equal("••••6789", SettingsRules.Mask("sk_live_123456789"));
        Assert.Equal("", SettingsRules.Mask(null));
        Assert.Equal("sk_live_123456789", SettingsRules.Unmask("••••6789", "sk_live_123456789"));
        Assert.Equal("new-key", SettingsRules.Unmask("new-key", "sk_live_123456789"));
        Assert.Equal("", SettingsRules.Unmask(null, "sk_live_123456789"));
    }

    [Fact]
    public void ASIC_card_keeps_the_stored_CVC_and_refuses_negative_fees()
    {
        var current = new AsicSettings { CardNumber = "4111111111111111", Cvc = "123" };
        var body = new AsicSettings { CardNumber = SettingsRules.Mask(current.CardNumber), Cvc = "", PaymentMethod = "nonsense", BpayAfterCardFailures = 99 };

        Assert.Null(SettingsRules.PrepareAsic(body, current));
        Assert.Equal("4111111111111111", body.CardNumber);
        Assert.Equal("123", body.Cvc);
        Assert.Equal(AsicPaymentMethods.Card, body.PaymentMethod);
        Assert.Equal(AsicSettings.MaxBpayAfterCardFailures, body.BpayAfterCardFailures);

        Assert.NotNull(SettingsRules.PrepareAsic(new AsicSettings { OneYearFee = -1 }, current));
    }

    [Fact]
    public void Test_mode_only_takes_Stripe_test_keys()
    {
        var current = new TestModeSettings();
        Assert.NotNull(SettingsRules.PrepareTestMode(new TestModeSettings { StripeSecretKey = "sk_live_x" }, current));
        Assert.NotNull(SettingsRules.PrepareTestMode(new TestModeSettings { StripePublishableKey = "pk_live_x" }, current));

        var ok = new TestModeSettings { StripeSecretKey = " sk_test_x ", StripePublishableKey = "pk_test_x", TestEmails = "A@x.com\n\na@x.com" };
        Assert.Null(SettingsRules.PrepareTestMode(ok, current));
        Assert.Equal("sk_test_x", ok.StripeSecretKey);
    }

    [Fact]
    public void Registry_settings_check_numbers_and_the_proxy_and_keep_masked_secrets()
    {
        var current = new AsicRegistryOptions { TwoCaptchaApiKey = "abcdef123456", ProxyPassword = "pw" };

        Assert.NotNull(SettingsRules.PrepareAsicRegistry(new AsicRegistrySection { MaxLookupsPerRun = -1 }, current));
        Assert.NotNull(SettingsRules.PrepareAsicRegistry(new AsicRegistrySection { ProxyUrl = "not a url" }, current));

        var body = new AsicRegistrySection { TwoCaptchaApiKey = "••••3456", ProxyPassword = "••••", ProxyUrl = " http://proxy:8080 " };
        Assert.Null(SettingsRules.PrepareAsicRegistry(body, current));
        Assert.Equal("abcdef123456", body.TwoCaptchaApiKey);
        Assert.Equal("pw", body.ProxyPassword);
        Assert.Equal("http://proxy:8080", body.ProxyUrl);
    }

    [Fact]
    public void Registration_needs_both_prices_before_it_opens()
    {
        Assert.NotNull(SettingsRules.PrepareRegistration(new RegistrationSettings { OneYearPrice = -5 }));
        Assert.NotNull(SettingsRules.PrepareRegistration(new RegistrationSettings { Enabled = true }));
        Assert.Null(SettingsRules.PrepareRegistration(new RegistrationSettings { Enabled = true, OneYearPrice = 50, ThreeYearPrice = 150 }));
    }

    [Fact]
    public void Portal_login_needs_a_business_email_and_falls_back_to_holder()
    {
        var current = new AsicPortalSettings { Password = "secret" };

        var (blank, error) = SettingsRules.PrepareAsicPortal(new SettingsRules.AsicPortalForm(true, "u", "••••", " ", null, null, null, null), current);
        Assert.NotNull(error);

        var (saved, ok) = SettingsRules.PrepareAsicPortal(new SettingsRules.AsicPortalForm(true, " u ", "••••", " a@b.com ", null, "nonsense", null, null), current);
        Assert.Null(ok);
        Assert.Equal("secret", saved.Password);
        Assert.Equal("u", saved.Username);
        Assert.Equal(Asic.Integrations.Portal.AsicDeclareAs.Holder, saved.DeclareAs);
        Assert.NotNull(blank);
    }

    [Fact]
    public void Key_inbox_fills_in_defaults()
    {
        var body = new AsicKeyInboxSettings { Username = " me@gmail.com ", Password = "••••", ImapPort = 0, AsicKeyPattern = "" };
        SettingsRules.PrepareAsicKeyInbox(body, new AsicKeyInboxSettings { Password = "app-password" });

        Assert.Equal("imap.gmail.com", body.ImapHost);
        Assert.Equal(993, body.ImapPort);
        Assert.Equal("me@gmail.com", body.Username);
        Assert.Equal("app-password", body.Password);
        Assert.Equal(AsicKeyInboxSettings.DefaultAsicKeyPattern, body.AsicKeyPattern);
    }

    [Fact]
    public void The_default_key_pattern_reads_every_sample_letter()
    {
        var check = SettingsJson(AsicKeyInboxTester.CheckPattern(AsicKeyInboxSettings.DefaultAsicKeyPattern));
        Assert.Contains("\"ok\":true", check);
    }

    [Fact]
    public void A_pattern_that_misses_a_letter_says_which()
    {
        Assert.Contains("renewal notice", SettingsJson(AsicKeyInboxTester.CheckPattern(@"Key for [^:]+: (\d-\d+)")));
        Assert.Contains("Invalid regex", SettingsJson(AsicKeyInboxTester.CheckPattern("(")));
    }

    private static string SettingsJson(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}
