using Renewtron.Data;
using Renewtron.Services;
using static Renewtron.Services.RenewalOrderProjection;

namespace Renewtron.Tests;

/// <summary>The ASIC name search before a renewal: retried on its own when ASIC doesn't answer.</summary>
public class SearchRetryTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 12)]
    [InlineData(4, 24)]
    [InlineData(5, 24)]
    public void The_wait_grows_with_each_failure(int failures, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), SearchRetry.Delay(failures));

    [Fact]
    public void It_stops_after_the_last_attempt() => Assert.Null(SearchRetry.Delay(SearchRetry.MaxAttempts));

    [Theory]
    [InlineData("Failed to initialize renewal session", false)]
    [InlineData("Search failed: An error occurred while sending the request.", false)]
    [InlineData("This business name is not due for renewal", true)]
    [InlineData("No business names found for this ABN", true)]
    [InlineData("Failed to search for business names. Please check the ABN and try again.", true)]
    public void Only_an_unanswered_search_is_tried_again(string message, bool answered) =>
        Assert.Equal(answered, SearchRetry.AsicAnswered(false, message));

    [Fact]
    public void A_search_waiting_to_be_tried_again_is_renewing_not_needs_person()
    {
        var at = new DateTime(2026, 11, 12, 14, 0, 0, DateTimeKind.Utc);
        var r = Compute(new Source(SourceState.Failed, Today, "ASIC search failed: Failed to initialize renewal session", NextSearchAt: at), [], Today);
        Assert.Equal(RenewalOrderStage.Renewing, r.Stage);
        Assert.Equal(at, r.NextActionAt);
    }

    [Fact]
    public void A_search_that_gave_up_needs_a_person()
    {
        var r = Compute(new Source(SourceState.Failed, Today, "ASIC search failed 6 times: Failed to initialize renewal session"), [], Today);
        Assert.Equal(RenewalOrderStage.NeedsPerson, r.Stage);
        Assert.Equal(RenewalOrderReason.AttemptsExhausted, r.Reason);
    }
}
