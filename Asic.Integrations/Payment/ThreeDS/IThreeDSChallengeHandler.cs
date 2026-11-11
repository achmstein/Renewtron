using AngleSharp.Html.Parser;
using Asic.Integrations.Shared;

namespace Asic.Integrations.Payment.ThreeDS;

/// <summary>
/// Provider-specific 3DS challenge handler. Implementations own the flow from
/// after the creq POST through cres finalisation back to the gateway. Selection
/// is by URL host via <see cref="CanHandle"/>.
/// </summary>
public interface IThreeDSChallengeHandler
{
    bool CanHandle(Uri challengeUrl);

    Task<StepResult<PaymentStepData>> HandleChallengeAsync(
        HttpClient httpClient,
        HtmlParser htmlParser,
        PaymentStepData data);
}
