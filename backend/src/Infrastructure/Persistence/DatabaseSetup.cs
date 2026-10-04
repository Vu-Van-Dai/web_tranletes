using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace ComicPlatform.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString, NpgsqlDataSource? dataSource = null)
    {
        if (dataSource is not null)
        {
            options.UseNpgsql(dataSource, ConfigureNpgsql);
        }
        else
        {
            options.UseNpgsql(connectionString, ConfigureNpgsql);
        }

        options.UseSnakeCaseNamingConvention();
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder o)
    {
        o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        o.MigrationsHistoryTable("__ef_migrations_history");
        o.CommandTimeout(30);
    }
}
