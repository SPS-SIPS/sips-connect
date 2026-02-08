using System.Security.Cryptography;
using System.Text;
using Aes = System.Security.Cryptography.Aes;

namespace SIPS.PostgreSQL.Security;

public static class EncryptionHelper
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("your-encryption-key-here"); // Must be 16, 24, or 32 bytes long
    private static readonly byte[] IV = Encoding.UTF8.GetBytes("your-iv-here"); // Must be 16 bytes long

    public static string Encrypt(string plainText)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = Key;
        aesAlg.IV = IV;

        ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

        using MemoryStream msEncrypt = new();
        using (CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (StreamWriter swEncrypt = new(csEncrypt))
        {
            swEncrypt.Write(plainText);
        }
        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    public static string Decrypt(string cipherText)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = Key;
        aesAlg.IV = IV;

        ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

        using MemoryStream msDecrypt = new(Convert.FromBase64String(cipherText));
        using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
        using StreamReader srDecrypt = new(csDecrypt);
        return srDecrypt.ReadToEnd();
    }
}
