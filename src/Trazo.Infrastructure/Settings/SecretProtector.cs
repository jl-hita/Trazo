using Microsoft.AspNetCore.DataProtection;

namespace Trazo.Infrastructure.Settings;

public interface ISecretProtector
{
    string Protect(string plaintext);
    /// <summary>Returns null when the value cannot be decrypted (e.g. keys were lost).</summary>
    string? Unprotect(string ciphertext);
}

public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Trazo.AppSettings.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string ciphertext)
    {
        try { return _protector.Unprotect(ciphertext); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
