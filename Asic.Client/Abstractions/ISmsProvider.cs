namespace Asic.Client.Abstractions;

public interface ISmsProvider
{
    Task<string> GetOtpAsync();
}
