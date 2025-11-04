namespace Asic.Client.Abstractions;

public interface ISmsProvider
{
    Task<bool> InitializeAsync();
    Task<string> GetOtpAsync();
}
