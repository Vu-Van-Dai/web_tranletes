using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Imaging;
using ComicPlatform.Infrastructure.Locking;
using ComicPlatform.Infrastructure.Persistence;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.Infrastructure.Security;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ComicPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, PlatformSettings settings, bool backgroundServices = true)
    {
        // Settings (validated by the host before this point)
        services.AddSingleton(settings);
        services.AddSingleton(settings.Jwt);
        services.AddSingleton(settings.PasswordHashing);
        services.AddSingleton(settings.Storage);
        services.AddSingleton(settings.Limits);
        services.AddSingleton(settings.Processing);
        services.AddSingleton(settings.R2);
        services.AddSingleton(settings.Cloudflare);
        services.AddSingleton(settings.Web);
        services.AddSingleton(new PublishingOptions
        {
            MaxUploadSize = settings.Limits.MaxUploadSize,
            PublishUploadTimeout = settings.Processing.PublishUploadTimeout,
        });
        services.AddSingleton(TimeProvider.System);

        // Database: one pooled data source shared by EF Core and raw Npgsql (advisory locks, health, batch upserts).
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(settings.DatabaseConnectionString);
            builder.EnableDynamicJson();
            return builder.Build();
        });

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddDbContext<AppDbContext>((sp, options) =>
            DatabaseSetup.Configure(options, settings.DatabaseConnectionString, sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();

        // Security
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();

        // Storage
        services.AddSingleton<StoragePaths>();
        services.AddSingleton<IStoragePaths>(sp => sp.GetRequiredService<StoragePaths>());
        services.AddSingleton<IPublicUrls, PublicUrls>();
        services.AddSingleton<IUploadStore, UploadStore>();
        services.AddSingleton<IChapterFiles, ChapterFiles>();
        services.AddHttpClient("cloudflare", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<ICdnPurger, CloudflareCdnPurger>();
        if (settings.Storage.IsLocalStore)
        {
            services.AddSingleton<LocalObjectStore>();
            services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<LocalObjectStore>());
        }
        else
        {
            services.AddSingleton<IObjectStore, R2ObjectStore>();
        }

        // Media processing
        services.AddSingleton<ProcessingMetrics>();
        services.AddSingleton<ImageWorkerPool>();
        services.AddSingleton<NetVipsImageProcessor>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<ProcessingQueue>();
        services.AddSingleton<IProcessingQueue>(sp => sp.GetRequiredService<ProcessingQueue>());
        services.AddSingleton<ChapterProcessingPipeline>();
        services.AddSingleton<StorageTaskProcessor>();
        services.AddSingleton<JanitorService>();
        services.AddSingleton<ViewFlushService>();

        if (backgroundServices)
        {
            services.AddHostedService<ProcessingHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<StorageTaskProcessor>());
            services.AddHostedService(sp => sp.GetRequiredService<JanitorService>());
            services.AddHostedService(sp => sp.GetRequiredService<ViewFlushService>());
        }

        return services;
    }
}
