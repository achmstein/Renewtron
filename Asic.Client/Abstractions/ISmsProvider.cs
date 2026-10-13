namespace Asic.Client.Abstractions;

public interface ISmsProvider
{
    Task<string> GetOtpAsync();

    /// <summary>
    /// Null when OTPs can be read right now; otherwise why not. Checked before a renewal
    /// starts at ASIC, because a renewal that can't read its OTP leaves an unpaid
    /// transaction at ASIC that blocks that name for 48 hours.
    /// </summary>
    Task<string?> CheckAsync();
}
