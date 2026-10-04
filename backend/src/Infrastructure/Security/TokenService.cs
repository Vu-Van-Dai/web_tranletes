using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ComicPlatform.Infrastructure.Security;

/// <summary>
/// Issues short-lived HS256 access tokens and opaque refresh tokens. The access token carries only the
/// subject and a UI hint ("sr" = system role); authorization decisions re-read roles from the database.
/// </summary>
public sealed class TokenService : ITokenService
{
    public const string SystemRoleClaim = "sr";

    private readonly JwtSettings _settings;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenService(JwtSettings settings, TimeProvider clock)
    {
        _settings = settings;
        _clock = clock;
        _credentials = new SigningCredentials(CreateSigningKey(settings), SecurityAlgorithms.HmacSha256);
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    public static SymmetricSecurityKey CreateSigningKey(JwtSettings settings) => new(settings.SigningKeyBytes);

    public static TokenValidationParameters CreateValidationParameters(JwtSettings settings) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(settings),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };

    public AccessToken CreateAccessToken(User user)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(SystemRoleClaim, user.SystemRole == SystemRole.SystemAdmin ? "admin" : "user"),
            ]),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    public string CreateOpaqueToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string HashOpaqueToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
