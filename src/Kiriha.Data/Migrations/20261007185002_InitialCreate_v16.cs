using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Data.Migrations;

/// <inheritdoc />
public partial class _20261007185002_InitialCreate_v16 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "anime_country_origin",
            columns: table => new
            {
                mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                country_code = table.Column<string>(type: "TEXT", nullable: false),
                fetched_at = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_anime_country_origin", x => x.mal_id);
            });

        migrationBuilder.CreateTable(
            name: "anime_relation_meta",
            columns: table => new
            {
                mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_anime_relation_meta", x => x.mal_id);
            });

        migrationBuilder.CreateTable(
            name: "anime_relations",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                source_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                relation_type = table.Column<string>(type: "TEXT", nullable: false),
                target_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                target_type = table.Column<string>(type: "TEXT", nullable: false),
                target_name = table.Column<string>(type: "TEXT", nullable: false),
                target_url = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_anime_relations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "episode_list_meta",
            columns: table => new
            {
                mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_episode_list_meta", x => x.mal_id);
            });

        migrationBuilder.CreateTable(
            name: "episode_releases",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                episode_number = table.Column<int>(type: "INTEGER", nullable: false),
                air_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                title = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_episode_releases", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "file_recognition_cache",
            columns: table => new
            {
                file_hash = table.Column<string>(type: "TEXT", nullable: false),
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                last_used = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_recognition_cache", x => x.file_hash);
            });

        migrationBuilder.CreateTable(
            name: "hidden_seasonal_anime",
            columns: table => new
            {
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hidden_seasonal_anime", x => x.anime_id);
            });

        migrationBuilder.CreateTable(
            name: "hidden_torrent_anime",
            columns: table => new
            {
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hidden_torrent_anime", x => x.anime_id);
            });

        migrationBuilder.CreateTable(
            name: "history",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                anime_title = table.Column<string>(type: "TEXT", nullable: false),
                russian_title = table.Column<string>(type: "TEXT", nullable: true),
                episode = table.Column<int>(type: "INTEGER", nullable: false),
                timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                action_type = table.Column<int>(type: "INTEGER", nullable: false),
                detail = table.Column<string>(type: "TEXT", nullable: false),
                tracker_status_json = table.Column<string>(type: "TEXT", nullable: true),
                poster_url = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_history", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "http_response_cache",
            columns: table => new
            {
                url_hash = table.Column<string>(type: "TEXT", nullable: false),
                etag = table.Column<string>(type: "TEXT", nullable: true),
                last_modified = table.Column<string>(type: "TEXT", nullable: true),
                body = table.Column<byte[]>(type: "BLOB", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_http_response_cache", x => x.url_hash);
            });

        migrationBuilder.CreateTable(
            name: "mal_search_cache",
            columns: table => new
            {
                query_normalized = table.Column<string>(type: "TEXT", nullable: false),
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                score = table.Column<float>(type: "REAL", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mal_search_cache", x => x.query_normalized);
            });

        migrationBuilder.CreateTable(
            name: "metadata",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                name = table.Column<string>(type: "TEXT", nullable: true),
                russian = table.Column<string>(type: "TEXT", nullable: true),
                description = table.Column<string>(type: "TEXT", nullable: true),
                episodes = table.Column<int>(type: "INTEGER", nullable: true),
                episodes_aired = table.Column<int>(type: "INTEGER", nullable: true),
                next_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_metadata", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sync_tasks",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                type = table.Column<string>(type: "TEXT", nullable: false),
                progress = table.Column<int>(type: "INTEGER", nullable: true),
                status = table.Column<string>(type: "TEXT", nullable: true),
                score = table.Column<int>(type: "INTEGER", nullable: true),
                payload = table.Column<string>(type: "TEXT", nullable: true),
                retry_count = table.Column<int>(type: "INTEGER", nullable: false),
                successful_trackers_json = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_tasks", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "torrent_title_filters",
            columns: table => new
            {
                anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                only_crunchyroll = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_netflix = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_amazon = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_hidive = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_varyg = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_erai_raws = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_toons_hub = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_judas = table.Column<bool>(type: "INTEGER", nullable: false),
                filter_hevc = table.Column<bool>(type: "INTEGER", nullable: false),
                filter1080p = table.Column<bool>(type: "INTEGER", nullable: false),
                use_custom_query = table.Column<bool>(type: "INTEGER", nullable: false),
                custom_query = table.Column<string>(type: "TEXT", nullable: true),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_torrent_title_filters", x => x.anime_id);
            });

        migrationBuilder.CreateTable(
            name: "user_anime",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false),
                media_kind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Anime"),
                chapters = table.Column<int>(type: "INTEGER", nullable: false),
                volumes = table.Column<int>(type: "INTEGER", nullable: false),
                chapters_read = table.Column<int>(type: "INTEGER", nullable: false),
                volumes_read = table.Column<int>(type: "INTEGER", nullable: false),
                aired_source_priority = table.Column<int>(type: "INTEGER", nullable: false),
                title = table.Column<string>(type: "TEXT", nullable: false),
                russian_title = table.Column<string>(type: "TEXT", nullable: true),
                status = table.Column<string>(type: "TEXT", nullable: false),
                progress = table.Column<int>(type: "INTEGER", nullable: false),
                total_episodes = table.Column<int>(type: "INTEGER", nullable: false),
                score = table.Column<string>(type: "TEXT", nullable: false),
                type = table.Column<string>(type: "TEXT", nullable: false),
                episodes_aired = table.Column<int>(type: "INTEGER", nullable: false),
                synopsis = table.Column<string>(type: "TEXT", nullable: true),
                russian_synopsis = table.Column<string>(type: "TEXT", nullable: true),
                main_picture_url = table.Column<string>(type: "TEXT", nullable: true),
                local_poster_path = table.Column<string>(type: "TEXT", nullable: true),
                nsfw = table.Column<string>(type: "TEXT", nullable: true),
                english_title = table.Column<string>(type: "TEXT", nullable: true),
                japanese_title = table.Column<string>(type: "TEXT", nullable: true),
                alternative_titles = table.Column<string>(type: "TEXT", nullable: false),
                genres = table.Column<string>(type: "TEXT", nullable: false),
                studios = table.Column<string>(type: "TEXT", nullable: false),
                status_detailed = table.Column<string>(type: "TEXT", nullable: true),
                mean_score = table.Column<string>(type: "TEXT", nullable: true),
                popularity = table.Column<int>(type: "INTEGER", nullable: false),
                rank = table.Column<int>(type: "INTEGER", nullable: true),
                airing_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                start_season = table.Column<string>(type: "TEXT", nullable: true),
                start_year = table.Column<int>(type: "INTEGER", nullable: true),
                rating = table.Column<string>(type: "TEXT", nullable: true),
                notes = table.Column<string>(type: "TEXT", nullable: true),
                is_rewatching = table.Column<bool>(type: "INTEGER", nullable: false),
                rewatch_count = table.Column<int>(type: "INTEGER", nullable: false),
                date_started = table.Column<DateTime>(type: "TEXT", nullable: true),
                date_completed = table.Column<DateTime>(type: "TEXT", nullable: true),
                broadcast_day = table.Column<string>(type: "TEXT", nullable: true),
                broadcast_time = table.Column<string>(type: "TEXT", nullable: true),
                last_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                last_episodes_sync = table.Column<DateTime>(type: "TEXT", nullable: true),
                next_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_anime", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_anime_relations_source_mal_id",
            table: "anime_relations",
            column: "source_mal_id");

        migrationBuilder.CreateIndex(
            name: "idx_episode_releases_mal_id",
            table: "episode_releases",
            column: "mal_id");

        migrationBuilder.CreateIndex(
            name: "idx_history_anime_id",
            table: "history",
            column: "anime_id");

        migrationBuilder.CreateIndex(
            name: "idx_history_timestamp",
            table: "history",
            column: "timestamp");

        migrationBuilder.CreateIndex(
            name: "idx_sync_tasks_anime_id",
            table: "sync_tasks",
            column: "anime_id");

        migrationBuilder.CreateIndex(
            name: "idx_user_anime_kind_status",
            table: "user_anime",
            columns: new[] { "media_kind", "status" });

        migrationBuilder.CreateIndex(
            name: "idx_user_anime_russian_title",
            table: "user_anime",
            column: "russian_title");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "anime_country_origin");

        migrationBuilder.DropTable(
            name: "anime_relation_meta");

        migrationBuilder.DropTable(
            name: "anime_relations");

        migrationBuilder.DropTable(
            name: "episode_list_meta");

        migrationBuilder.DropTable(
            name: "episode_releases");

        migrationBuilder.DropTable(
            name: "file_recognition_cache");

        migrationBuilder.DropTable(
            name: "hidden_seasonal_anime");

        migrationBuilder.DropTable(
            name: "hidden_torrent_anime");

        migrationBuilder.DropTable(
            name: "history");

        migrationBuilder.DropTable(
            name: "http_response_cache");

        migrationBuilder.DropTable(
            name: "mal_search_cache");

        migrationBuilder.DropTable(
            name: "metadata");

        migrationBuilder.DropTable(
            name: "sync_tasks");

        migrationBuilder.DropTable(
            name: "torrent_title_filters");

        migrationBuilder.DropTable(
            name: "user_anime");
    }
}
