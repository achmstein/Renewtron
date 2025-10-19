using Asic.Client.Captcha;

namespace Asic.Client.Models;

public class SearchResult<T>
{
    public bool Success { get; set; }
    public bool RequiresCaptcha { get; set; }
    public CaptchaChallenge CaptchaChallenge { get; set; }
    public T Data { get; set; }

    public static SearchResult<T> SuccessResult(T data) => new()
    {
        Success = true,
        RequiresCaptcha = false,
        Data = data
    };

    public static SearchResult<T> CaptchaRequired(CaptchaChallenge challenge) => new()
    {
        Success = false,
        RequiresCaptcha = true,
        CaptchaChallenge = challenge
    };

    public static SearchResult<T> Failed() => new()
    {
        Success = false,
        RequiresCaptcha = false
    };
}
