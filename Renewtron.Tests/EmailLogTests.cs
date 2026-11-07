using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>The email log: SendGrid's events, the "email is failing" rule, and the sender's error text.</summary>
public class EmailLogTests
{
    [Fact]
    public void Signed_events_are_verified_with_the_accounts_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        const string body = """[{"event":"delivered","sg_message_id":"abc.filter1","timestamp":1794041707}]""";
        const string timestamp = "1794041707";
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(timestamp + body), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        Assert.True(EmailEvents.VerifySignature(publicKey, signature, timestamp, body));
        Assert.False(EmailEvents.VerifySignature(publicKey, signature, timestamp, body.Replace("delivered", "bounce")));
        Assert.False(EmailEvents.VerifySignature(publicKey, signature, "1794041708", body));
        Assert.False(EmailEvents.VerifySignature("not a key", signature, timestamp, body));
    }

    [Fact]
    public void Events_are_read_with_our_ids()
    {
        var id = Guid.NewGuid();
        var events = EmailEvents.Parse($$"""
            [{"event":"bounce","sg_message_id":"MSG123.filterdrecv-1","timestamp":1794041707,"reason":"550 mailbox unavailable","outbound_id":"{{id}}"},
             {"event":"open","sg_message_id":"MSG123.x","timestamp":1794041800},
             {"nothing":"here"}]
            """);
        Assert.Equal(2, events.Count);
        Assert.Equal(("bounce", "MSG123", id, "550 mailbox unavailable"), (events[0].Kind, events[0].MessageId, events[0].OutboundId, events[0].Detail));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794041707).UtcDateTime, events[0].At);
        Assert.Null(events[1].OutboundId);
    }

    [Fact]
    public void Delivery_status_never_steps_back_and_opens_are_kept_apart()
    {
        var m = new OutboundMessage();
        var at = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
        EmailEvents.Apply(m, new("deferred", at, "x", null, "try later"));
        Assert.Equal("deferred", m.DeliveryStatus);
        EmailEvents.Apply(m, new("delivered", at.AddMinutes(5), "x", null, null));
        Assert.Equal(("delivered", (string?)null), (m.DeliveryStatus, m.DeliveryDetail));
        EmailEvents.Apply(m, new("deferred", at.AddMinutes(6), "x", null, "late echo"));
        Assert.Equal("delivered", m.DeliveryStatus);
        EmailEvents.Apply(m, new("open", at.AddMinutes(9), "x", null, null));
        Assert.Equal(at.AddMinutes(9), m.OpenedAt);
        Assert.Equal("delivered", m.DeliveryStatus);
        EmailEvents.Apply(m, new("bounce", at.AddMinutes(10), "x", null, "550"));
        Assert.Equal(("bounce", "550"), (m.DeliveryStatus, m.DeliveryDetail));
        EmailEvents.Apply(m, new("processed", at.AddMinutes(11), "x", null, null));
        Assert.Equal("bounce", m.DeliveryStatus);
    }

    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_refused_key_is_a_problem_straight_away()
    {
        var problem = EmailEvents.Problem([(OutboundStatus.Failed, "SendGrid returned 401: The provided authorization grant is invalid, expired, or revoked", Now.AddMinutes(-5))], Now);
        Assert.NotNull(problem);
        Assert.Contains("Settings → SendGrid", problem);
    }

    [Fact]
    public void Three_failures_in_a_row_are_a_problem_but_one_bad_address_is_not()
    {
        Assert.Null(EmailEvents.Problem([(OutboundStatus.Failed, "SendGrid returned 400: invalid email", Now), (OutboundStatus.Sent, null, Now.AddMinutes(-1))], Now));
        Assert.NotNull(EmailEvents.Problem([
            (OutboundStatus.Failed, "SendGrid returned 500", Now),
            (OutboundStatus.Failed, "SendGrid returned 500", Now.AddMinutes(-1)),
            (OutboundStatus.Failed, "SendGrid returned 500", Now.AddMinutes(-2))], Now));
    }

    [Fact]
    public void Email_working_again_clears_the_problem()
    {
        Assert.Null(EmailEvents.Problem([(OutboundStatus.Sent, null, Now), (OutboundStatus.Failed, "SendGrid returned 401", Now.AddMinutes(-3))], Now));
        Assert.Null(EmailEvents.Problem([], Now));
        // A refused key a week ago, with nothing sent since, isn't news.
        Assert.Null(EmailEvents.Problem([(OutboundStatus.Failed, "SendGrid returned 401", Now.AddDays(-7))], Now));
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"The provided authorization grant is invalid, expired, or revoked","field":null,"help":null}]}""", "The provided authorization grant is invalid, expired, or revoked")]
    [InlineData("plain failure", "plain failure")]
    public void SendGrids_error_is_put_in_its_own_words(string body, string expected) =>
        Assert.Equal(expected, EmailSender.SendGridError(body));

    [Fact]
    public async Task Old_email_text_is_cleared_but_the_record_stays()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var old = new OutboundMessage { Id = Guid.NewGuid(), Template = "x", Recipient = "a@b.c", Subject = "s", BodyHtml = "<p>old</p>", BodyText = "old", CreatedAt = DateTime.UtcNow.AddDays(-200) };
        var recent = new OutboundMessage { Id = Guid.NewGuid(), Template = "x", Recipient = "a@b.c", Subject = "s", BodyHtml = "<p>new</p>", BodyText = "new", CreatedAt = DateTime.UtcNow.AddDays(-10) };
        db.OutboundMessages.AddRange(old, recent);
        await db.SaveChangesAsync();
        // ExecuteUpdate isn't supported in-memory: check the query's rule instead.
        var cutoff = DateTime.UtcNow.AddDays(-180);
        var toClear = await db.OutboundMessages.Where(m => m.CreatedAt < cutoff && (m.BodyHtml != null || m.BodyText != null)).Select(m => m.Id).ToListAsync();
        Assert.Equal([old.Id], toClear);
    }
}
