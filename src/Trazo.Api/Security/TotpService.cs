using System.Security.Cryptography;
using System.Text;

namespace Trazo.Api.Security;

public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string BuildProvisioningUri(string secret, string email, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{email}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    public static bool VerifyCode(string secret, string suppliedCode, DateTimeOffset now)
    {
        var code = new string(suppliedCode.Where(char.IsDigit).ToArray());
        if (code.Length != 6) return false;
        byte[] key;
        try { key = Base32Decode(secret); }
        catch (FormatException) { return false; }

        var counter = now.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = GenerateCode(key, counter + offset);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    public static string[] GenerateRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).Insert(5, "-"))
            .ToArray();

    public static string NormalizeRecoveryCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Base32Encode(byte[] data)
    {
        var output = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0) output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var cleaned = value.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in cleaned)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0) throw new FormatException("Invalid base32 secret.");
            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 255));
                bitsLeft -= 8;
            }
        }
        return output.ToArray();
    }

    private static string GenerateCode(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(counter));
        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }
}
