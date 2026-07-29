using System.Collections.Generic;
using System.Text.Json;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kiriha.Services.Data.Core.EntityConfigurations;

public static class ConfigurationHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static void ConfigureJsonList<TEntity>(EntityTypeBuilder<TEntity> entity, System.Linq.Expressions.Expression<System.Func<TEntity, List<string>?>> propertyExpression) where TEntity : class
    {
        entity.Property(propertyExpression)
              .HasConversion(
                  v => JsonSerializer.Serialize(v, JsonOptions),
                  v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>()
              );
    }
}

public class AnimeItemConfiguration : IEntityTypeConfiguration<AnimeItem>
{
    public void Configure(EntityTypeBuilder<AnimeItem> builder)
    {
        builder.ToTable("user_anime");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.MediaKind)
              .HasConversion<string>()
              .HasDefaultValue(MediaKind.Anime);

        builder.Property(e => e.Status)
              .HasConversion(
                  v => StatusMapper.ToDbString(v),
                  v => StatusMapper.FromDbString(v)
              );

        ConfigurationHelpers.ConfigureJsonList(builder, e => e.Genres);
        ConfigurationHelpers.ConfigureJsonList(builder, e => e.Studios);
        ConfigurationHelpers.ConfigureJsonList(builder, e => e.AlternativeTitles);

        builder.Ignore(e => e.Season);
        builder.HasIndex(e => e.RussianTitle).HasDatabaseName("idx_user_anime_russian_title");
    }
}

public class HistoryItemConfiguration : IEntityTypeConfiguration<HistoryItem>
{
    public void Configure(EntityTypeBuilder<HistoryItem> builder)
    {
        builder.ToTable("history");
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
    }
}

public class FileRecognitionCacheConfiguration : IEntityTypeConfiguration<FileRecognitionCache>
{
    public void Configure(EntityTypeBuilder<FileRecognitionCache> builder)
    {
        builder.HasKey(e => e.FileHash);
    }
}

public class MalSearchCacheConfiguration : IEntityTypeConfiguration<MalSearchCache>
{
    public void Configure(EntityTypeBuilder<MalSearchCache> builder)
    {
        builder.ToTable("mal_search_cache");
        builder.HasKey(e => e.QueryNormalized);
    }
}

public class HttpCacheEntryConfiguration : IEntityTypeConfiguration<HttpCacheEntry>
{
    public void Configure(EntityTypeBuilder<HttpCacheEntry> builder)
    {
        builder.ToTable("http_response_cache");
        builder.HasKey(e => e.UrlHash);
    }
}

public class EpisodeListMetaConfiguration : IEntityTypeConfiguration<EpisodeListMeta>
{
    public void Configure(EntityTypeBuilder<EpisodeListMeta> builder)
    {
        builder.ToTable("episode_list_meta");
        builder.HasKey(e => e.MalId);
        builder.Property(e => e.MalId).ValueGeneratedNever();
    }
}

public class EpisodeReleaseConfiguration : IEntityTypeConfiguration<EpisodeRelease>
{
    public void Configure(EntityTypeBuilder<EpisodeRelease> builder)
    {
        builder.ToTable("episode_releases");
        builder.HasIndex(e => e.MalId).HasDatabaseName("idx_episode_releases_mal_id");
    }
}

public class SyncTaskEntityConfiguration : IEntityTypeConfiguration<SyncTaskEntity>
{
    public void Configure(EntityTypeBuilder<SyncTaskEntity> builder)
    {
        builder.ToTable("sync_tasks");
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
    }
}

public class AnimeRelationConfiguration : IEntityTypeConfiguration<AnimeRelation>
{
    public void Configure(EntityTypeBuilder<AnimeRelation> builder)
    {
        builder.ToTable("anime_relations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.SourceMalId).HasDatabaseName("idx_anime_relations_source_mal_id");
    }
}

public class AnimeRelationMetaConfiguration : IEntityTypeConfiguration<AnimeRelationMeta>
{
    public void Configure(EntityTypeBuilder<AnimeRelationMeta> builder)
    {
        builder.ToTable("anime_relation_meta");
        builder.HasKey(e => e.MalId);
        builder.Property(e => e.MalId).ValueGeneratedNever();
    }
}

public class AnimeStaffConfiguration : IEntityTypeConfiguration<AnimeStaff>
{
    public void Configure(EntityTypeBuilder<AnimeStaff> builder)
    {
        builder.ToTable("anime_staff");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.SourceMalId).HasDatabaseName("idx_anime_staff_source_mal_id");
    }
}

public class AnimeStaffMetaConfiguration : IEntityTypeConfiguration<AnimeStaffMeta>
{
    public void Configure(EntityTypeBuilder<AnimeStaffMeta> builder)
    {
        builder.ToTable("anime_staff_meta");
        builder.HasKey(e => e.MalId);
        builder.Property(e => e.MalId).ValueGeneratedNever();
    }
}
