using Trazo.Api.Security;
using Xunit;

namespace Trazo.UnitTests;

public sealed class TotpServiceTests
{
    [Fact]
    public void VerifyCode_AcceptsRfc6238Sha1TestVector()
    {
        // RFC 6238 test secret and the 59-second test timestamp, truncated to six digits.
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.True(TotpService.VerifyCode(secret, "287082", timestamp));
        Assert.True(TotpService.TryGetMatchingStep(secret, "287082", timestamp, out var step));
        Assert.Equal(1, step);
    }

    [Fact]
    public void VerifyCode_RejectsIncorrectOrMalformedCodes()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.False(TotpService.VerifyCode(secret, "000000", timestamp));
        Assert.False(TotpService.VerifyCode(secret, "12345", timestamp));
        Assert.False(TotpService.VerifyCode(secret, "ABCDEF", timestamp));
    }

    [Fact]
    public void RecoveryCodes_AreUniqueAndNormalized()
    {
        var codes = TotpService.GenerateRecoveryCodes();

        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Equal(10, TotpService.NormalizeRecoveryCode(code).Length));
    }

    [Fact]
    public void ProvisioningUri_ContainsIssuerAndAccount()
    {
        var uri = TotpService.BuildProvisioningUri("JBSWY3DPEHPK3PXP", "athlete@example.com", "TRAZO");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("issuer=TRAZO", uri);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri);
    }
}
