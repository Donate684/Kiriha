using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Core;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    internal SqliteMigrationLock? ActiveMigrationLock { get; set; }

    public DbSet<AnimeEntity> UserAnime { get; set; } = null!;
    public DbSet<HistoryItem> History { get; set; } = null!;
    public DbSet<ShikiMetadata> Metadata { get; set; } = null!;
    public DbSet<SyncTaskEntity> SyncTasks { get; set; } = null!;
    public DbSet<MalSearchCache> MalSearchCache { get; set; } = null!;
    public DbSet<HttpCacheEntry> HttpResponseCache { get; set; } = null!;
    public DbSet<AnimeRelation> AnimeRelations { get; set; } = null!;
    public DbSet<AnimeRelationMeta> AnimeRelationMeta { get; set; } = null!;
    public DbSet<HiddenSeasonalAnime> HiddenSeasonalAnime { get; set; } = null!;
    public DbSet<HiddenTorrentAnime> HiddenTorrentAnime { get; set; } = null!;
    public DbSet<TorrentTitleFilter> TorrentTitleFilters { get; set; } = null!;
    public DbSet<AnimeCountryOrigin> AnimeCountryOrigin { get; set; } = null!;
    public DbSet<TitleMappingEntity> TitleMappings { get; set; } = null!;
    public DbSet<ScrobbleProgressEntity> ScrobbleProgress { get; set; } = null!;
    public DbSet<PlaybackResumeEntryEntity> PlaybackResume { get; set; } = null!;
    public DbSet<AniListMediaMapEntity> AniListMediaMap { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(SqliteMigrationLock.OwnerInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all configurations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Automated Naming (PascalCase -> snake_case)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName != null) entity.SetTableName(ToSnakeCase(tableName));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return SnakeCaseRegex().Replace(input, "$1_$2").ToLowerInvariant();
    }

    [GeneratedRegex(@"([a-z0-9])([A-Z])", RegexOptions.None, 500)]
    private static partial Regex SnakeCaseRegex();
}
