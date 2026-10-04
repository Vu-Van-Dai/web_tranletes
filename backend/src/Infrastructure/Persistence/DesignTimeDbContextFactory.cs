using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ComicPlatform.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var env = DotEnv.LoadWithoutOverridingEnvironment(Directory.GetCurrentDirectory());
            env.TryGetValue("DATABASE_CONNECTION_STRING", out connectionString);
        }

        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseSetup.Configure(options, connectionString ?? "Host=localhost;Database=comicplatform_design");
        return new AppDbContext(options.Options, new TenantContext());
    }
}
