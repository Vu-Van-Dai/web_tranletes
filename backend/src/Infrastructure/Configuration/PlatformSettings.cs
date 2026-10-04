using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace ComicPlatform.Infrastructure.Configuration;

/// <summary>
/// All runtime settings, bound from the flat environment variable names documented in .env.example.
/// Validated at startup (fail fast) — see <see cref="Validate"/>.
/// </summary>
public sealed class PlatformSettings
{
    public string DatabaseConnectionString { get; init; } = "";
    public JwtSettings Jwt { get; init; } = new();
    public PasswordHashSettings PasswordHashing { get; init; } = new();
    public SeedAdminSettings SeedAdmin { get; init; } = new();
    public StorageSettings Storage { get; init; } = new();
    public LimitSettings Limits { get; init; } = new();
    public ProcessingSettings Processing { get; init; } = new();
    public R2Settings R2 { get; init; } = new();
    public CloudflareSettings Cloudflare { get; init; } = new();
    public WebSettings Web { get; init; } = new();

    public static PlatformSettings FromConfiguration(IConfiguration c)
    {
        return new PlatformSettings
        {
            DatabaseConnectionString = c["DATABASE_CONNECTION_STRING"] ?? "",
            Jwt = new JwtSettings
            {
                Secret = c["JWT_SECRET"] ?? "",
                Issuer = Str(c, "JWT_ISSUER", "comicplatform"),
                Audience = Str(c, "JWT_AUDIENCE", "comicplatform-web"),
                AccessTokenMinutes = Int(c, "ACCESS_TOKEN_MINUTES", 10),
                RefreshTokenDays = Int(c, "REFRESH_TOKEN_DAYS", 14),
            },
            PasswordHashing = new PasswordHashSettings
            {
                MemoryKiB = Int(c, "ARGON2_MEMORY_KB", 19_456),
                Iterations = Int(c, "ARGON2_ITERATIONS", 2),
                Parallelism = Int(c, "ARGON2_PARALLELISM", 1),
                MaxConcurrentHashes = Int(c, "ARGON2_MAX_CONCURRENCY", 2),
            },
            SeedAdmin = new SeedAdminSettings
            {
                Email = c["SEED_ADMIN_EMAIL"],
                Username = c["SEED_ADMIN_USERNAME"],
                Password = c["SEED_ADMIN_PASSWORD"],
            },
            Storage = new StorageSettings
            {
                ComicStoragePath = Str(c, "COMIC_STORAGE_PATH", "/data/comics"),
                TempStoragePath = Str(c, "TEMP_STORAGE_PATH", "/data/tmp"),
                PackageStore = Str(c, "PACKAGE_STORE", "r2").ToLowerInvariant(),
                PublicImageUrl = (c["PUBLIC_IMAGE_URL"] ?? "").TrimEnd('/'),
                OriginSharedSecret = c["ORIGIN_SHARED_SECRET"] ?? "",
                MinFreeSpaceBytes = Size(c, "MIN_FREE_SPACE", 2L * 1024 * 1024 * 1024),
            },
            Limits = new LimitSettings
            {
                MaxUploadSize = Size(c, "MAX_UPLOAD_SIZE", 200L * 1024 * 1024),
                MaxExtractedSize = Size(c, "MAX_EXTRACTED_SIZE", 600L * 1024 * 1024),
                MaxPagesPerChapter = Int(c, "MAX_PAGES_PER_CHAPTER", 200),
                MaxImagePixels = Long(c, "MAX_IMAGE_PIXELS", 40_000_000),
                MaxImageDimension = Int(c, "MAX_IMAGE_DIMENSION", 16_383),
                MaxEntrySize = Size(c, "MAX_ENTRY_SIZE", 50L * 1024 * 1024),
                MaxCompressionRatio = Int(c, "MAX_COMPRESSION_RATIO", 100),
                PageMaxWidth = Int(c, "PAGE_MAX_WIDTH", 2000),
                WebpQuality = Int(c, "WEBP_QUALITY", 82),
                MaxCoverSize = Size(c, "MAX_COVER_SIZE", 8L * 1024 * 1024),
            },
            Processing = new ProcessingSettings
            {
                ImageConcurrency = Int(c, "IMAGE_PROCESSING_CONCURRENCY", 2),
                QueueCapacity = Int(c, "PROCESSING_QUEUE_CAPACITY", 50),
                JobTimeout = Time(c, "PROCESSING_JOB_TIMEOUT", TimeSpan.FromMinutes(15)),
                UploadRetention = Time(c, "UPLOAD_RETENTION", TimeSpan.FromHours(24)),
                DeletedComicRetention = Time(c, "DELETED_COMIC_RETENTION", TimeSpan.FromDays(30)),
                JanitorInterval = Time(c, "JANITOR_INTERVAL", TimeSpan.FromHours(1)),
                OutboxInterval = Time(c, "OUTBOX_INTERVAL", TimeSpan.FromSeconds(15)),
                PublishUploadTimeout = Time(c, "PUBLISH_UPLOAD_TIMEOUT", TimeSpan.FromMinutes(2)),
            },
            R2 = new R2Settings
            {
                AccountId = c["R2_ACCOUNT_ID"] ?? "",
                AccessKey = c["R2_ACCESS_KEY"] ?? "",
                SecretKey = c["R2_SECRET_KEY"] ?? "",
                Bucket = c["R2_BUCKET"] ?? "",
                Endpoint = c["R2_ENDPOINT"] ?? "",
            },
            Cloudflare = new CloudflareSettings
            {
                ZoneId = c["CLOUDFLARE_ZONE_ID"] ?? "",
                ApiToken = c["CLOUDFLARE_API_TOKEN"] ?? "",
            },
            Web = new WebSettings
            {
                CorsAllowedOrigins = (c["CORS_ALLOWED_ORIGINS"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(o => o.TrimEnd('/'))
                    .ToArray(),
                TrustCloudflareHeaders = Bool(c, "TRUST_CLOUDFLARE_HEADERS", false),
                SecureCookies = Bool(c, "SECURE_COOKIES", true),
                RateLimitsEnabled = Bool(c, "RATE_LIMITS_ENABLED", true),
                MigrateOnStartup = Bool(c, "DATABASE_MIGRATE_ON_STARTUP", true),
                LogDirectory = c["LOG_DIRECTORY"],
            },
        };
    }

    /// <summary>Returns human-readable configuration problems (empty when valid).</summary>
    public IReadOnlyList<string> Validate(bool isProduction)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DatabaseConnectionString))
        {
            errors.Add("DATABASE_CONNECTION_STRING is required.");
        }

        if (Jwt.SigningKeyBytes.Length < 32)
        {
            errors.Add("JWT_SECRET must be at least 32 bytes (base64 or plain text).");
        }

        if (Storage.PackageStore is not ("r2" or "local"))
        {
            errors.Add("PACKAGE_STORE must be 'r2' or 'local'.");
        }

        if (Storage.PackageStore == "r2" && (string.IsNullOrWhiteSpace(R2.Bucket) || string.IsNullOrWhiteSpace(R2.AccessKey) || string.IsNullOrWhiteSpace(R2.SecretKey) || string.IsNullOrWhiteSpace(R2.ResolvedEndpoint)))
        {
            errors.Add("PACKAGE_STORE=r2 requires R2_BUCKET, R2_ACCESS_KEY, R2_SECRET_KEY and R2_ENDPOINT (or R2_ACCOUNT_ID).");
        }

        if (isProduction && Storage.PackageStore == "local")
        {
            errors.Add("PACKAGE_STORE=local is for development/E2E only.");
        }

        if (isProduction && Storage.OriginSharedSecret.Length < 24)
        {
            errors.Add("ORIGIN_SHARED_SECRET must be at least 24 characters in production.");
        }

        if (isProduction && Web.CorsAllowedOrigins.Length == 0)
        {
            errors.Add("CORS_ALLOWED_ORIGINS is required in production.");
        }

        if (Processing.ImageConcurrency is < 1 or > 16)
        {
            errors.Add("IMAGE_PROCESSING_CONCURRENCY must be between 1 and 16.");
        }

        if (Limits.MaxPagesPerChapter is < 1 or > 2000)
        {
            errors.Add("MAX_PAGES_PER_CHAPTER must be between 1 and 2000.");
        }

        if (Limits.WebpQuality is < 30 or > 100)
        {
            errors.Add("WEBP_QUALITY must be between 30 and 100.");
        }

        if (Limits.MaxImageDimension > 16_383)
        {
            errors.Add("MAX_IMAGE_DIMENSION cannot exceed 16383 (WebP limit).");
        }

        return errors;
    }

    private static string Str(IConfiguration c, string key, string fallback) =>
        string.IsNullOrWhiteSpace(c[key]) ? fallback : c[key]!.Trim();

    private static int Int(IConfiguration c, string key, int fallback) =>
        int.TryParse(c[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static long Long(IConfiguration c, string key, long fallback) =>
        long.TryParse(c[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool Bool(IConfiguration c, string key, bool fallback) =>
        bool.TryParse(c[key], out var v) ? v : fallback;

    private static TimeSpan Time(IConfiguration c, string key, TimeSpan fallback) =>
        TimeSpan.TryParse(c[key], CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static long Size(IConfiguration c, string key, long fallback) =>
        ParseSize(c[key]) ?? fallback;

    /// <summary>Parses "1048576", "512KB", "200MB", "1.5GB" (binary multiples).</summary>
    public static long? ParseSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim().ToUpperInvariant();
        long multiplier = 1;
        foreach (var (suffix, mult) in new[] { ("GB", 1L << 30), ("MB", 1L << 20), ("KB", 1L << 10), ("B", 1L) })
        {
            if (v.EndsWith(suffix, StringComparison.Ordinal))
            {
                multiplier = mult;
                v = v[..^suffix.Length].Trim();
                break;
            }
        }

        return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number > 0
            ? (long)(number * multiplier)
            : null;
    }
}

public sealed class JwtSettings
{
    public string Secret { get; init; } = "";
    public string Issuer { get; init; } = "comicplatform";
    public string Audience { get; init; } = "comicplatform-web";
    public int AccessTokenMinutes { get; init; } = 10;
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Base64 secrets are decoded; anything else is used as UTF-8 bytes.</summary>
    public byte[] SigningKeyBytes
    {
        get
        {
            if (string.IsNullOrEmpty(Secret))
            {
                return [];
            }

            var buffer = new byte[Secret.Length];
            return Convert.TryFromBase64String(Secret, buffer, out var written) && written >= 32
                ? buffer[..written]
                : System.Text.Encoding.UTF8.GetBytes(Secret);
        }
    }
}

public sealed class PasswordHashSettings
{
    public int MemoryKiB { get; init; } = 19_456;
    public int Iterations { get; init; } = 2;
    public int Parallelism { get; init; } = 1;
    public int MaxConcurrentHashes { get; init; } = 2;
}

public sealed class SeedAdminSettings
{
    public string? Email { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

public sealed class StorageSettings
{
    public string ComicStoragePath { get; init; } = "/data/comics";
    public string TempStoragePath { get; init; } = "/data/tmp";
    public string PackageStore { get; init; } = "r2";
    public string PublicImageUrl { get; init; } = "";
    public string OriginSharedSecret { get; init; } = "";
    public long MinFreeSpaceBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public bool IsLocalStore => PackageStore == "local";
}

public sealed class LimitSettings
{
    public long MaxUploadSize { get; init; }
    public long MaxExtractedSize { get; init; }
    public int MaxPagesPerChapter { get; init; }
    public long MaxImagePixels { get; init; }
    public int MaxImageDimension { get; init; }
    public long MaxEntrySize { get; init; }
    public int MaxCompressionRatio { get; init; }
    public int PageMaxWidth { get; init; }
    public int WebpQuality { get; init; }
    public long MaxCoverSize { get; init; }
}

public sealed class ProcessingSettings
{
    public int ImageConcurrency { get; init; } = 2;
    public int QueueCapacity { get; init; } = 50;
    public TimeSpan JobTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan UploadRetention { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan DeletedComicRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan JanitorInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan OutboxInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan PublishUploadTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed class R2Settings
{
    public string AccountId { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string Endpoint { get; init; } = "";

    public string ResolvedEndpoint => !string.IsNullOrWhiteSpace(Endpoint)
        ? Endpoint.TrimEnd('/')
        : string.IsNullOrWhiteSpace(AccountId) ? "" : $"https://{AccountId}.r2.cloudflarestorage.com";
}

public sealed class CloudflareSettings
{
    public string ZoneId { get; init; } = "";
    public string ApiToken { get; init; } = "";

    public bool PurgeEnabled => !string.IsNullOrWhiteSpace(ZoneId) && !string.IsNullOrWhiteSpace(ApiToken);
}

public sealed class WebSettings
{
    public string[] CorsAllowedOrigins { get; init; } = [];
    public bool TrustCloudflareHeaders { get; init; }
    public bool SecureCookies { get; init; } = true;
    public bool RateLimitsEnabled { get; init; } = true;
    public bool MigrateOnStartup { get; init; } = true;
    public string? LogDirectory { get; init; }
}
