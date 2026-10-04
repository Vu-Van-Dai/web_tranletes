using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ComicPlatform.Infrastructure.Persistence;

/// <summary>
/// Startup tasks: apply migrations under an advisory lock (safe if two instances start together),
/// create storage directories and seed the first SystemAdmin from SEED_ADMIN_*.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceProvider services,
    PlatformSettings settings,
    StoragePaths paths,
    NpgsqlDataSource dataSource,
    ILogger<DatabaseInitializer> logger)
{
    private const long MigrationLockId = 7_301_284_190_001;

    public async Task InitializeAsync(CancellationToken ct)
    {
        paths.EnsureBaseDirectories();

        if (settings.Web.MigrateOnStartup)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using (var lockCmd = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLockId})", connection))
            {
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                    await db.Database.MigrateAsync(ct);
                }
            }
            finally
            {
                await using var unlockCmd = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationLockId})", connection);
                await unlockCmd.ExecuteNonQueryAsync(ct);
            }
        }

        await SeedAdminAsync(ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.SystemRole == SystemRole.SystemAdmin, ct))
        {
            return;
        }

        var seed = settings.SeedAdmin;
        if (!seed.IsConfigured)
        {
            logger.LogWarning("No SystemAdmin exists and SEED_ADMIN_* is not configured; admin features are unreachable until one is created.");
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var normalizedEmail = User.Normalize(seed.Email!);
        var normalizedUsername = User.Normalize(seed.Username!);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail || u.NormalizedUsername == normalizedUsername, ct);
        if (existing is not null)
        {
            existing.PromoteToSystemAdmin(clock.GetUtcNow());
            logger.LogInformation("Promoted existing user {UserId} to SystemAdmin from SEED_ADMIN_*", existing.Id);
        }
        else
        {
            var hash = await hasher.HashAsync(seed.Password!, ct);
            var admin = new User(seed.Username!.Trim().ToLowerInvariant(), seed.Email!, "Quản trị hệ thống", hash, SystemRole.SystemAdmin, clock.GetUtcNow());
            db.Users.Add(admin);
            logger.LogInformation("Seeded SystemAdmin {UserId}", admin.Id);
        }

        await db.SaveChangesAsync(ct);
    }
}
