using Kiriha.Services.Data.Core;

using Kiriha.Core.Domain.Models;
using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Kiriha.Infrastructure.Http;

namespace Kiriha.Services.Data.Core;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AnimeEntity> UserAnime { get; set; } = null!;
    public DbSet<HistoryItem> History { get; set; } = null!;
    public DbSet<ShikiMetadata> Metadata { get; set; } = null!;
    public DbSet<SyncTaskEntity> SyncTasks { get; set; } = null!;
    public DbSet<FileRecognitionCache> FileRecognitionCache { get; set; } = null!;
    public DbSet<EpisodeRelease> EpisodeReleases { get; set; } = null!;
    public DbSet<MalSearchCache> MalSearchCache { get; set; } = null!;
    public DbSet<HttpCacheEntry> HttpResponseCache { get; set; } = null!;
    public DbSet<EpisodeListMeta> EpisodeListMeta { get; set; } = null!;
    public DbSet<AnimeRelation> AnimeRelations { get; set; } = null!;
    public DbSet<AnimeRelationMeta> AnimeRelationMeta { get; set; } = null!;
    public DbSet<AnimeStaff> AnimeStaff { get; set; } = null!;
    public DbSet<AnimeStaffMeta> AnimeStaffMeta { get; set; } = null!;

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
        return Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
