using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayApp.Core.Attempt2;

public class SingleKeyDataProtector : IDataProtector
{
    private readonly byte[] _key;

    public SingleKeyDataProtector(byte[] key)
    {
        if (key.Length != 32) // AES-256 key size
        {
            throw new ArgumentException("Key length must be 32 bytes.");
        }

        _key = key;
    }

    public IDataProtector CreateProtector(string purpose)
    {
        using var hmac = new HMACSHA256(_key);
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var key = hmac.ComputeHash(purposeBytes).Take(32).ToArray();
        return new SingleKeyDataProtector(key);
    }

    public byte[] Protect(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        byte[] iv = aes.IV;
        byte[] encrypted = aes.EncryptCbc(plaintext, iv);

        byte[] result = new byte[iv.Length + encrypted.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);

        return result;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        using var aes = Aes.Create();
        aes.Key = _key;

        byte[] iv = new byte[16];
        byte[] cipherText = new byte[protectedData.Length - iv.Length];

        Buffer.BlockCopy(protectedData, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(protectedData, iv.Length, cipherText, 0, cipherText.Length);

        return aes.DecryptCbc(cipherText, iv);
    }

}