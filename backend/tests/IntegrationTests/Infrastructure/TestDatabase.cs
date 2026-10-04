using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ComicPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Creates one database per test class by cloning a freshly migrated template. Only databases whose
/// name starts with <see cref="Prefix"/> are ever created or dropped — other databases on the shared
/// PostgreSQL server are never touched.
/// </summary>
public static class TestDatabase
{
    public const string Prefix = "comicplatform_test_";
    private const string TemplateName = Prefix + "template";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _templateReady;

    public static string AdminConnectionString
    {
        get
        {
            var cs = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(cs))
            {
                var env = DotEnv.LoadWithoutOverridingEnvironment(AppContext.BaseDirectory);
                env.TryGetValue("DATABASE_CONNECTION_STRING", out cs);
                cs ??= Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");
            }

            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException("Set TEST_DATABASE_CONNECTION_STRING or DATABASE_CONNECTION_STRING (root .env) for integration tests.");
            }

            return new NpgsqlConnectionStringBuilder(cs) { Database = "postgres", Pooling = false }.ConnectionString;
        }
    }

    public static async Task<(string Name, string ConnectionString)> CreateAsync()
    {
        await EnsureTemplateAsync();
        var name = Prefix + Guid.NewGuid().ToString("N")[..16];
        await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"");
        var cs = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name, Pooling = true, MaxPoolSize = 40 }.ConnectionString;
        return (name, cs);
    }

    public static async Task DropAsync(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to drop non-test database '{name}'.");
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static async Task EnsureTemplateAsync()
    {
        if (_templateReady)
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            if (_templateReady)
            {
                return;
            }

            // Leftovers from crashed runs.
            await using (var conn = new NpgsqlConnection(AdminConnectionString))
            {
                await conn.OpenAsync();
                var stale = new List<string>();
                await using (var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname LIKE 'comicplatform\\_test\\_%'", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        stale.Add(reader.GetString(0));
                    }
                }

                foreach (var db in stale.Where(d => d.StartsWith(Prefix, StringComparison.Ordinal)))
                {
                    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)", conn);
                    await drop.ExecuteNonQueryAsync();
                }
            }

            await ExecuteAsync($"CREATE DATABASE \"{TemplateName}\" ENCODING 'UTF8' TEMPLATE template0");
            var templateCs = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = TemplateName }.ConnectionString;
            var options = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseSetup.Configure(options, templateCs);
            await using (var db = new AppDbContext(options.Options, new TenantContext()))
            {
                await db.Database.MigrateAsync();
            }

            NpgsqlConnection.ClearAllPools();
            _templateReady = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync();
    }
}
