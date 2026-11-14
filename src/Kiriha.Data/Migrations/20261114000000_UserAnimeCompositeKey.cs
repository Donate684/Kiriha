using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Data.Migrations;

public partial class UserAnimeCompositeKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE TABLE ""user_anime_dg_tmp"" (
                ""id"" INTEGER NOT NULL,
                ""media_kind"" TEXT NOT NULL DEFAULT 'Anime',
                ""chapters"" INTEGER NOT NULL,
                ""volumes"" INTEGER NOT NULL,
                ""chapters_read"" INTEGER NOT NULL,
                ""volumes_read"" INTEGER NOT NULL,
                ""aired_source_priority"" INTEGER NOT NULL,
                ""title"" TEXT NOT NULL,
                ""russian_title"" TEXT NULL,
                ""status"" TEXT NOT NULL,
                ""progress"" INTEGER NOT NULL,
                ""total_episodes"" INTEGER NOT NULL,
                ""score"" TEXT NOT NULL,
                ""type"" TEXT NOT NULL,
                ""episodes_aired"" INTEGER NOT NULL,
                ""synopsis"" TEXT NULL,
                ""russian_synopsis"" TEXT NULL,
                ""main_picture_url"" TEXT NULL,
                ""local_poster_path"" TEXT NULL,
                ""nsfw"" TEXT NULL,
                ""english_title"" TEXT NULL,
                ""japanese_title"" TEXT NULL,
                ""alternative_titles"" TEXT NOT NULL,
                ""genres"" TEXT NOT NULL,
                ""studios"" TEXT NOT NULL,
                ""status_detailed"" TEXT NULL,
                ""mean_score"" TEXT NULL,
                ""popularity"" INTEGER NOT NULL,
                ""rank"" INTEGER NULL,
                ""airing_date"" TEXT NULL,
                ""start_season"" TEXT NULL,
                ""start_year"" INTEGER NULL,
                ""rating"" TEXT NULL,
                ""notes"" TEXT NULL,
                ""is_rewatching"" INTEGER NOT NULL,
                ""rewatch_count"" INTEGER NOT NULL,
                ""date_started"" TEXT NULL,
                ""date_completed"" TEXT NULL,
                ""broadcast_day"" TEXT NULL,
                ""broadcast_time"" TEXT NULL,
                ""last_episode_at"" TEXT NULL,
                ""last_episodes_sync"" TEXT NULL,
                ""next_episode_at"" TEXT NULL,
                CONSTRAINT ""PK_user_anime"" PRIMARY KEY (""id"", ""media_kind"")
            );

            INSERT INTO ""user_anime_dg_tmp"" SELECT
                ""id"", ""media_kind"", ""chapters"", ""volumes"", ""chapters_read"", ""volumes_read"",
                ""aired_source_priority"", ""title"", ""russian_title"", ""status"", ""progress"",
                ""total_episodes"", ""score"", ""type"", ""episodes_aired"", ""synopsis"", ""russian_synopsis"",
                ""main_picture_url"", ""local_poster_path"", ""nsfw"", ""english_title"", ""japanese_title"",
                ""alternative_titles"", ""genres"", ""studios"", ""status_detailed"", ""mean_score"",
                ""popularity"", ""rank"", ""airing_date"", ""start_season"", ""start_year"", ""rating"",
                ""notes"", ""is_rewatching"", ""rewatch_count"", ""date_started"", ""date_completed"",
                ""broadcast_day"", ""broadcast_time"", ""last_episode_at"", ""last_episodes_sync"",
                ""next_episode_at""
            FROM ""user_anime"";

            DROP TABLE ""user_anime"";

            ALTER TABLE ""user_anime_dg_tmp"" RENAME TO ""user_anime"";

            CREATE INDEX ""idx_user_anime_kind_status"" ON ""user_anime"" (""media_kind"", ""status"");
            CREATE INDEX ""idx_user_anime_russian_title"" ON ""user_anime"" (""russian_title"");
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE TABLE ""user_anime_dg_tmp"" (
                ""id"" INTEGER NOT NULL,
                ""media_kind"" TEXT NOT NULL DEFAULT 'Anime',
                ""chapters"" INTEGER NOT NULL,
                ""volumes"" INTEGER NOT NULL,
                ""chapters_read"" INTEGER NOT NULL,
                ""volumes_read"" INTEGER NOT NULL,
                ""aired_source_priority"" INTEGER NOT NULL,
                ""title"" TEXT NOT NULL,
                ""russian_title"" TEXT NULL,
                ""status"" TEXT NOT NULL,
                ""progress"" INTEGER NOT NULL,
                ""total_episodes"" INTEGER NOT NULL,
                ""score"" TEXT NOT NULL,
                ""type"" TEXT NOT NULL,
                ""episodes_aired"" INTEGER NOT NULL,
                ""synopsis"" TEXT NULL,
                ""russian_synopsis"" TEXT NULL,
                ""main_picture_url"" TEXT NULL,
                ""local_poster_path"" TEXT NULL,
                ""nsfw"" TEXT NULL,
                ""english_title"" TEXT NULL,
                ""japanese_title"" TEXT NULL,
                ""alternative_titles"" TEXT NOT NULL,
                ""genres"" TEXT NOT NULL,
                ""studios"" TEXT NOT NULL,
                ""status_detailed"" TEXT NULL,
                ""mean_score"" TEXT NULL,
                ""popularity"" INTEGER NOT NULL,
                ""rank"" INTEGER NULL,
                ""airing_date"" TEXT NULL,
                ""start_season"" TEXT NULL,
                ""start_year"" INTEGER NULL,
                ""rating"" TEXT NULL,
                ""notes"" TEXT NULL,
                ""is_rewatching"" INTEGER NOT NULL,
                ""rewatch_count"" INTEGER NOT NULL,
                ""date_started"" TEXT NULL,
                ""date_completed"" TEXT NULL,
                ""broadcast_day"" TEXT NULL,
                ""broadcast_time"" TEXT NULL,
                ""last_episode_at"" TEXT NULL,
                ""last_episodes_sync"" TEXT NULL,
                ""next_episode_at"" TEXT NULL,
                CONSTRAINT ""PK_user_anime"" PRIMARY KEY (""id"")
            );

            INSERT INTO ""user_anime_dg_tmp"" SELECT * FROM ""user_anime"";
            DROP TABLE ""user_anime"";
            ALTER TABLE ""user_anime_dg_tmp"" RENAME TO ""user_anime"";

            CREATE INDEX ""idx_user_anime_kind_status"" ON ""user_anime"" (""media_kind"", ""status"");
            CREATE INDEX ""idx_user_anime_russian_title"" ON ""user_anime"" (""russian_title"");
        ");
    }
}
