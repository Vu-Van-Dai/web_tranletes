using ComicPlatform.Application.Abstractions;
using Npgsql;

namespace ComicPlatform.Infrastructure.Locking;

/// <summary>
/// Session-level PostgreSQL advisory lock on a dedicated pooled connection.
/// Serializes e.g. "publish" against "withdraw from object store" for the same package.
/// </summary>
public sealed class PostgresAdvisoryLock(NpgsqlDataSource dataSource) : IDistributedLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var connection = await dataSource.OpenConnectionAsync(ct);
        try
        {
            await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@k, 0))", connection))
            {
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            return new Releaser(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Releaser(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@k, 0))", connection);
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
