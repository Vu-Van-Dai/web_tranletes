using ComicPlatform.Application.Analytics;
using ComicPlatform.Application.Admin;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Identity;
using ComicPlatform.Application.Library;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Application.Settings;
using ComicPlatform.Application.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ComicPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<SettingsService>();
        services.AddScoped<AuthService>();

        // Tenancy
        services.AddScoped<HouseAccessService>();
        services.AddScoped<TenantResourceGuard>();
        services.AddScoped<MemberManager>();
        services.AddScoped<HouseProfileService>();

        // Catalog / publishing
        services.AddScoped<TagService>();
        services.AddScoped<HouseComicService>();
        services.AddScoped<HouseChapterService>();
        services.AddScoped<PublicCatalogService>();
        services.AddScoped<StorageOutbox>();
        services.AddScoped<ComicStatsUpdater>();
        services.AddScoped<PublishingService>();

        // Admin
        services.AddScoped<AdminHouseService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<AdminStatsService>();
        services.AddScoped<AdsService>();

        // Reader library (follows, history, views)
        services.AddScoped<LibraryService>();
        services.AddSingleton<ViewCounter>();
        services.AddSingleton<ViewTicketService>();
        services.AddScoped<ViewService>();
        services.AddScoped<ViewReportService>();
        return services;
    }
}
