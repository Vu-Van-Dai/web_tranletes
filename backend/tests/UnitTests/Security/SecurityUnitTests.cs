using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ComicPlatform.UnitTests.Security;

public class PasswordHasherTests
{
    private static Argon2PasswordHasher Hasher(int memory = 1024, int iterations = 1) =>
        new(new PasswordHashSettings { MemoryKiB = memory, Iterations = iterations, Parallelism = 1, MaxConcurrentHashes = 2 });

    [Fact]
    public async Task Hash_verifies_and_uses_phc_argon2id_format()
    {
        var hasher = Hasher();
        var hash = await hasher.HashAsync("mật-khẩu-rất-mạnh-123");
        Assert.StartsWith("$argon2id$v=19$m=1024,t=1,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordCheck.Success, await hasher.VerifyAsync(hash, "mật-khẩu-rất-mạnh-123"));
        Assert.Equal(PasswordCheck.Failed, await hasher.VerifyAsync(hash, "mật-khẩu-rất-mạnh-124"));
        Assert.NotEqual(hash, await hasher.HashAsync("mật-khẩu-rất-mạnh-123")); // random salt
    }

    [Fact]
    public async Task Weaker_parameters_request_a_rehash()
    {
        var weak = await Hasher(memory: 512).HashAsync("password-123456");
        Assert.Equal(PasswordCheck.SuccessRehashNeeded, await Hasher(memory: 1024).VerifyAsync(weak, "password-123456"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("$argon2id$v=19$m=999999999,t=1,p=1$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsdHNhbHQ$aGFzaA")]
    public async Task Malformed_hashes_never_verify(string stored) =>
        Assert.Equal(PasswordCheck.Failed, await Hasher().VerifyAsync(stored, "anything"));
}

public class TokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Secret = Convert.ToBase64String(Enumerable.Range(0, 48).Select(i => (byte)i).ToArray()),
        Issuer = "test",
        Audience = "test-web",
        AccessTokenMinutes = 10,
    };

    private static User NewUser() => new("docgia", "docgia@example.com", "Độc giả", "hash", SystemRole.User, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Access_token_validates_and_carries_only_subject_and_hint()
    {
        var user = NewUser();
        var token = new TokenService(Settings, TimeProvider.System).CreateAccessToken(user);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, TokenService.CreateValidationParameters(Settings));
        Assert.True(result.IsValid);
        Assert.Equal(user.Id.ToString("D"), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("user", result.Claims[TokenService.SystemRoleClaim]);
    }

    [Fact]
    public async Task Tampered_wrong_key_and_alg_none_tokens_are_rejected()
    {
        var service = new TokenService(Settings, TimeProvider.System);
        var token = service.CreateAccessToken(NewUser()).Token;
        var parameters = TokenService.CreateValidationParameters(Settings);
        var handler = new JsonWebTokenHandler();

        var parts = token.Split('.');
        var tampered = parts[0] + "." + Base64UrlEncoder.Encode("{\"sub\":\"" + Guid.NewGuid() + "\",\"sr\":\"admin\"}") + "." + parts[2];
        Assert.False((await handler.ValidateTokenAsync(tampered, parameters)).IsValid);

        var unsigned = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + parts[1] + ".";
        Assert.False((await handler.ValidateTokenAsync(unsigned, parameters)).IsValid);

        var otherKey = new JwtSettings { Secret = Convert.ToBase64String(new byte[48]), Issuer = "test", Audience = "test-web" };
        var foreign = new TokenService(otherKey, TimeProvider.System).CreateAccessToken(NewUser()).Token;
        Assert.False((await handler.ValidateTokenAsync(foreign, parameters)).IsValid);
    }

    [Fact]
    public void Opaque_tokens_are_random_and_hashed_deterministically()
    {
        var service = new TokenService(Settings, TimeProvider.System);
        var a = service.CreateOpaqueToken();
        Assert.NotEqual(a, service.CreateOpaqueToken());
        Assert.Equal(service.HashOpaqueToken(a), service.HashOpaqueToken(a));
        Assert.Equal(64, service.HashOpaqueToken(a).Length);
    }

    [Fact]
    public void Short_secrets_fail_validation()
    {
        var settings = new PlatformSettings { DatabaseConnectionString = "x", Jwt = new JwtSettings { Secret = "too-short" }, Storage = new StorageSettings { PackageStore = "local" } };
        Assert.Contains(settings.Validate(isProduction: false), e => e.Contains("JWT_SECRET", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("200MB", 200L * 1024 * 1024)]
    [InlineData("1.5GB", 1610612736L)]
    [InlineData("512kb", 524288L)]
    [InlineData("1048576", 1048576L)]
    public void Sizes_parse_with_suffixes(string text, long expected) => Assert.Equal(expected, PlatformSettings.ParseSize(text));
}
