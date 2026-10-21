using ComicPlatform.Domain.Auditing;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Library;
using ComicPlatform.Domain.Publishing;
using ComicPlatform.Domain.Settings;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ComicPlatform.Application.Abstractions;

/// <summary>
/// Persistence port. Tenant-owned sets (Comics, Chapters, ChapterPackages, ChapterPages, ProcessingJobs)
/// carry a fail-closed "Tenant" query filter: without a resolved house context they return nothing.
/// Cross-tenant readers must opt out explicitly with <c>IgnoreQueryFilters([QueryFilters.Tenant])</c>.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<TranslationHouse> TranslationHouses { get; }
    DbSet<TranslationHouseMember> TranslationHouseMembers { get; }
    DbSet<Comic> Comics { get; }
    DbSet<Tag> Tags { get; }
    DbSet<ComicTag> ComicTags { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<ChapterPackage> ChapterPackages { get; }
    DbSet<ChapterPage> ChapterPages { get; }
    DbSet<ProcessingJob> ProcessingJobs { get; }
    DbSet<StorageTask> StorageTasks { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<Follow> Follows { get; }
    DbSet<ReadingProgress> ReadingProgress { get; }
    DbSet<ComicViewDaily> ComicViewDaily { get; }
    DbSet<Domain.Analytics.ChapterViewDaily> ChapterViewDaily { get; }
    DbSet<Domain.Analytics.ViewReceipt> ViewReceipts { get; }
    DbSet<Domain.Analytics.ViewRejectionDaily> ViewRejectionDaily { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
