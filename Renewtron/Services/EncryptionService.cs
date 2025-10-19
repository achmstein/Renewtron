using System.Security.Cryptography;
using System.Text;

namespace Renewtron.Services;

public class EncryptionService : IEncryptionService
{
    // WARNING: In production, store this key securely (Azure Key Vault, AWS Secrets Manager, etc.)
    // DO NOT hardcode it in the source code!
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public EncryptionService(IConfiguration configuration)
    {
        // In production, load these from secure configuration
        var keyString = configuration["Encryption:Key"] ?? "ThisIsA32ByteKeyForEncryption!!"; // 32 bytes
        var ivString = configuration["Encryption:IV"] ?? "ThisIsA16ByteIV!"; // 16 bytes

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