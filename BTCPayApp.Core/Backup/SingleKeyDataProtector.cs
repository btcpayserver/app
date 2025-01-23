using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayApp.Core.Backup;

public class SingleKeyDataProtector : IDataProtector
{
    private readonly byte[] _key;

    public SingleKeyDataProtector(byte[] key)
    {
        if (key.Length != 32) // AES-256 key size
            throw new ArgumentException("Key length must be 32 bytes.");

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

        var iv = aes.IV;
        var encrypted = aes.EncryptCbc(plaintext, iv);

        return iv.Concat(encrypted).ToArray();
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        using var aes = Aes.Create();
        aes.Key = _key;

        if (protectedData.Length == 0)
            return protectedData;

        var iv = protectedData.Take(16).ToArray();
        var cipherText = protectedData.Skip(16).ToArray();

        return aes.DecryptCbc(cipherText, iv);
    }
}
