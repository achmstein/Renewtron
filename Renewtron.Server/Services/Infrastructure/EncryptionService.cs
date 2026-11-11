using Renewtron.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace Renewtron.Services;

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public EncryptionService(IConfiguration configuration)
    {
        // Key/IV must come from secure configuration (Encryption__Key / Encryption__IV
        // environment variables, user secrets, a vault, ...) — never committed to source.
        // Fail loudly rather than silently encrypting with a padded/empty key.
        var keyString = configuration["Encryption:Key"];
        var ivString = configuration["Encryption:IV"];

        if (string.IsNullOrWhiteSpace(keyString) || string.IsNullOrWhiteSpace(ivString))
        {
            throw new InvalidOperationException(
                "Encryption:Key and Encryption:IV are not configured. " +
                "Set the Encryption__Key (32 chars) and Encryption__IV (16 chars) environment variables.");
        }

        _key = Encoding.UTF8.GetBytes(keyString.PadRight(32).Substring(0, 32));
        _iv = Encoding.UTF8.GetBytes(ivString.PadRight(16).Substring(0, 16));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        using var msEncrypt = new MemoryStream();
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plainText);
        }

        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

        using var msDecrypt = new MemoryStream(Convert.FromBase64String(cipherText));
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);

        return srDecrypt.ReadToEnd();
    }
}