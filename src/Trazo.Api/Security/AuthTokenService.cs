using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Security;

public sealed class AuthTokenService(IConfiguration configuration)
{
    public const int AccessTokenMinutes = 15;
    public const int RefreshTokenDays = 30;

    public string CreateAccessToken(TrazoUser user)
    {
        var issuer = configuration["Authentication:Jwt:Issuer"]
            ?? throw new InvalidOperationException("Authentication:Jwt:Issuer is required.");
        var audience = configuration["Authentication:Jwt:Audience"]
            ?? throw new InvalidOperationException("Authentication:Jwt:Audience is required.");
        var key = GetSigningKey();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<(string AccessToken, string RefreshToken)> CreateTokenPairAsync(
        TrazoUser user, TrazoDbContext db, string? deviceName, CancellationToken cancellationToken)
    {
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var now = DateTimeOffset.UtcNow;
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(RefreshTokenDays),
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim()[..Math.Min(deviceName.Trim().Length, 120)]
        });
        await db.SaveChangesAsync(cancellationToken);
        return (CreateAccessToken(user), rawRefreshToken);
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private SymmetricSecurityKey GetSigningKey()
    {
        var configured = configuration["Authentication:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(configured) || Encoding.UTF8.GetByteCount(configured) < 32)
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured));
    }

    public TokenValidationParameters GetValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = configuration["Authentication:Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = configuration["Authentication:Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = GetSigningKey(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
        RoleClaimType = ClaimTypes.Role
    };
}
