using Microsoft.Extensions.Options;
using TwoCaptcha.Captcha;

namespace Asic.Client.Captcha;

public class TwoCaptchaSolver : ICaptchaSolver
{
    private readonly string _apiKey;
    private readonly int _defaultTimeout;
    private readonly int _recaptchaTimeout;
    private readonly int _pollingInterval;

    public TwoCaptchaSolver(IOptions<TwoCaptchaSettings> options)
    {
        var settings = options.Value;

        _apiKey = settings.ApiKey ?? throw new ArgumentNullException(nameof(settings.ApiKey));
        _defaultTimeout = settings.DefaultTimeout;
        _recaptchaTimeout = settings.RecaptchaTimeout;
        _pollingInterval = settings.PollingInterval;
    }

    public async Task<string> SolveAsync(CaptchaChallenge challenge)
    {
        try
        {
            var solver = new TwoCaptcha.TwoCaptcha(_apiKey)
            {
                DefaultTimeout = _defaultTimeout,
                RecaptchaTimeout = _recaptchaTimeout,
                PollingInterval = _pollingInterval
            };

            var captcha = new ReCaptcha();
            captcha.SetSiteKey(challenge.SiteKey);
            captcha.SetUrl(challenge.CaptchaUrl);
            captcha.SetInvisible(true);
            captcha.SetEnterprise(false);
            captcha.SetAction("userverify");

            await solver.Solve(captcha);

            return captcha.Code;
        }
        catch (Exception ex)
        {
            // Log the exception if needed
            return null;
        }
    }
}