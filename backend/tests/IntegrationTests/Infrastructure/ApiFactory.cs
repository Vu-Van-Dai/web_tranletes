using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ComicPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API (all middleware, auth, background processing with NetVips) against a dedicated
/// PostgreSQL database and a private temp storage directory. PACKAGE_STORE=local stands in for R2.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminLogin = "quantri";
    public const string AdminPassword = "Admin-Test-Password-123";
    public const string AllowedOrigin = "http://localhost:5173";

    private string _dbName = "";

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "cp-it-" + Guid.NewGuid().ToString("N"));

    public string ConnectionString { get; private set; } = "";

    /// <summary>Per-fixture overrides (e.g. small upload limits or rate limits on).</summary>
    protected virtual IDictionary<string, string?> Overrides => new Dictionary<string, string?>();

    async Task IAsyncLifetime.InitializeAsync()
    {
        (_dbName, ConnectionString) = await TestDatabase.CreateAsync();
        Directory.CreateDirectory(StorageRoot);
        _ = Server; // start the host (runs migrations check + admin seed)
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await TestDatabase.DropAsync(_dbName);
        try
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["DATABASE_CONNECTION_STRING"] = ConnectionString,
            ["JWT_SECRET"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["JWT_ISSUER"] = "comicplatform-test",
            ["JWT_AUDIENCE"] = "comicplatform-test-web",
            ["ARGON2_MEMORY_KB"] = "1024",
            ["ARGON2_ITERATIONS"] = "1",
            ["SEED_ADMIN_EMAIL"] = "admin@tramtruyen.test",
            ["SEED_ADMIN_USERNAME"] = AdminLogin,
            ["SEED_ADMIN_PASSWORD"] = AdminPassword,
            ["PACKAGE_STORE"] = "local",
            ["COMIC_STORAGE_PATH"] = Path.Combine(StorageRoot, "comics"),
            ["TEMP_STORAGE_PATH"] = Path.Combine(StorageRoot, "tmp"),
            ["PUBLIC_IMAGE_URL"] = "/cdn",
            ["ORIGIN_SHARED_SECRET"] = "integration-origin-secret-1234567890",
            ["CORS_ALLOWED_ORIGINS"] = AllowedOrigin,
            ["SECURE_COOKIES"] = "false",
            ["RATE_LIMITS_ENABLED"] = "false",
            ["MIN_FREE_SPACE"] = "1MB",
            ["IMAGE_PROCESSING_CONCURRENCY"] = "2",
            ["OUTBOX_INTERVAL"] = "00:00:01",
            ["MAX_UPLOAD_SIZE"] = "50MB",
            ["MAX_PAGES_PER_CHAPTER"] = "50",
            // Opt-in host logs for debugging test failures.
            ["LOG_DIRECTORY"] = Environment.GetEnvironmentVariable("TEST_LOG_DIRECTORY"),
        };

        foreach (var (key, value) in Overrides)
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton(this));
    }
}

public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?> { ["RATE_LIMITS_ENABLED"] = "true" };
}

public sealed class SmallLimitsApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["MAX_UPLOAD_SIZE"] = "200KB",
        ["MAX_PAGES_PER_CHAPTER"] = "3",
        ["PROCESSING_QUEUE_CAPACITY"] = "50",
    };
}
