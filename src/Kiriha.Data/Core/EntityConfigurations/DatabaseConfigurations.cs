using System.Text.Json;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kiriha.Data.Core.EntityConfigurations;

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

public class AnimeEntityConfiguration : IEntityTypeConfiguration<AnimeEntity>
{
    public void Configure(EntityTypeBuilder<AnimeEntity> builder)
    {
        builder.ToTable("user_anime");
        builder.HasKey(e => new { e.Id, e.MediaKind });
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

        builder.HasIndex(e => e.RussianTitle).HasDatabaseName("idx_user_anime_russian_title");
        builder.HasIndex(e => new { e.MediaKind, e.Status }).HasDatabaseName("idx_user_anime_kind_status");
    }
}

public class HistoryItemConfiguration : IEntityTypeConfiguration<HistoryItem>
{
    public void Configure(EntityTypeBuilder<HistoryItem> builder)
    {
        builder.ToTable("history");
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.MediaKind)
               .HasConversion<string>()
               .HasDefaultValue(MediaKind.Anime);
        builder.Property(e => e.TrackerStatusJson).HasColumnName("tracker_status_json");
        builder.Property(e => e.PosterUrl).HasColumnName("poster_url");
        builder.HasIndex(e => e.Timestamp).HasDatabaseName("idx_history_timestamp");
        builder.HasIndex(e => e.AnimeId).HasDatabaseName("idx_history_anime_id");
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

public class SyncTaskEntityConfiguration : IEntityTypeConfiguration<SyncTaskEntity>
{
    public void Configure(EntityTypeBuilder<SyncTaskEntity> builder)
    {
        builder.ToTable("sync_tasks");
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.MediaKind)
               .HasConversion<string>()
               .HasDefaultValue(MediaKind.Anime);
        builder.HasIndex(e => e.AnimeId).HasDatabaseName("idx_sync_tasks_anime_id");
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


public class HiddenSeasonalAnimeConfiguration : IEntityTypeConfiguration<HiddenSeasonalAnime>
{
    public void Configure(EntityTypeBuilder<HiddenSeasonalAnime> builder)
    {
        builder.ToTable("hidden_seasonal_anime");
        builder.HasKey(e => e.AnimeId);
        builder.Property(e => e.AnimeId).ValueGeneratedNever();
    }
}

public class HiddenTorrentAnimeConfiguration : IEntityTypeConfiguration<HiddenTorrentAnime>
{
    public void Configure(EntityTypeBuilder<HiddenTorrentAnime> builder)
    {
        builder.ToTable("hidden_torrent_anime");
        builder.HasKey(e => e.AnimeId);
        builder.Property(e => e.AnimeId).ValueGeneratedNever();
    }
}

public class TorrentTitleFilterConfiguration : IEntityTypeConfiguration<TorrentTitleFilter>
{
    public void Configure(EntityTypeBuilder<TorrentTitleFilter> builder)
    {
        builder.ToTable("torrent_title_filters");
        builder.HasKey(e => e.AnimeId);
        builder.Property(e => e.AnimeId).ValueGeneratedNever();
    }
}

public class AnimeCountryOriginConfiguration : IEntityTypeConfiguration<AnimeCountryOrigin>
{
    public void Configure(EntityTypeBuilder<AnimeCountryOrigin> builder)
    {
        builder.ToTable("anime_country_origin");
        builder.HasKey(e => e.MalId);
        builder.Property(e => e.MalId).ValueGeneratedNever();
        builder.Property(e => e.CountryCode).IsRequired();
        builder.Property(e => e.FetchedAt).IsRequired();
    }
}

public class ShikiMetadataConfiguration : IEntityTypeConfiguration<ShikiMetadata>
{
    public void Configure(EntityTypeBuilder<ShikiMetadata> builder)
    {
        builder.ToTable("metadata");
        builder.HasKey(e => new { e.Id, e.MediaKind });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MediaKind)
               .HasConversion<string>()
               .HasDefaultValue(MediaKind.Anime);
    }
}

public class TitleMappingEntityConfiguration : IEntityTypeConfiguration<TitleMappingEntity>
{
    public void Configure(EntityTypeBuilder<TitleMappingEntity> builder)
    {
        builder.ToTable("title_mappings");
        builder.HasKey(e => e.NormalizedTitle);
        builder.Property(e => e.MediaKind)
               .HasConversion<string>()
               .HasDefaultValue(MediaKind.Anime);
    }
}

public class ScrobbleProgressEntityConfiguration : IEntityTypeConfiguration<ScrobbleProgressEntity>
{
    public void Configure(EntityTypeBuilder<ScrobbleProgressEntity> builder)
    {
        builder.ToTable("scrobble_progress");
        builder.HasKey(e => e.Key);
    }
}

public class PlaybackResumeEntryEntityConfiguration : IEntityTypeConfiguration<PlaybackResumeEntryEntity>
{
    public void Configure(EntityTypeBuilder<PlaybackResumeEntryEntity> builder)
    {
        builder.ToTable("playback_resume");
        builder.HasKey(e => e.VideoUrl);
    }
}

public class AniListMediaMapEntityConfiguration : IEntityTypeConfiguration<AniListMediaMapEntity>
{
    public void Configure(EntityTypeBuilder<AniListMediaMapEntity> builder)
    {
        builder.ToTable("anilist_media_map");
        builder.HasKey(e => new { e.PrimaryId, e.MediaType });
        builder.Property(e => e.PrimaryId).ValueGeneratedNever();
    }
}
