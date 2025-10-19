namespace Asic.Client.Captcha;

public interface ICaptchaSolver
{
    Task<string> SolveAsync(CaptchaChallenge challenge);
}
