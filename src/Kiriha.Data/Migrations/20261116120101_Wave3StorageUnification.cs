using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Data.Migrations;

/// <inheritdoc />
public partial class _20261116120101_Wave3StorageUnification : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE TABLE ""metadata_dg_tmp"" (
                ""id"" INTEGER NOT NULL,
                ""media_kind"" TEXT NOT NULL DEFAULT 'Anime',
                ""name"" TEXT NULL,
                ""russian"" TEXT NULL,
                ""description"" TEXT NULL,
                ""episodes"" INTEGER NULL,
                ""episodes_aired"" INTEGER NULL,
                ""next_episode_at"" TEXT NULL,
                ""fetched_at"" TEXT NOT NULL,
                ""poster_url"" TEXT NULL,
                CONSTRAINT ""PK_metadata"" PRIMARY KEY (""id"", ""media_kind"")
            );

            INSERT OR IGNORE INTO ""metadata_dg_tmp"" (
                ""id"", ""media_kind"", ""name"", ""russian"", ""description"",
                ""episodes"", ""episodes_aired"", ""next_episode_at"", ""fetched_at"", ""poster_url""
            )
            SELECT
                CASE
                    WHEN (""id"" & 1073741824) != 0 THEN (""id"" & ~1073741824)
                    WHEN (""id"" & 536870912) != 0 THEN (""id"" & ~536870912)
                    ELSE ""id""
                END,
                CASE
                    WHEN (""id"" & 1073741824) != 0 THEN 'Manga'
                    WHEN (""id"" & 536870912) != 0 THEN 'LightNovel'
                    ELSE 'Anime'
                END,
                ""name"", ""russian"", ""description"",
                ""episodes"", ""episodes_aired"", ""next_episode_at"", ""fetched_at"", ""poster_url""
            FROM ""metadata"";

            DROP TABLE ""metadata"";

            ALTER TABLE ""metadata_dg_tmp"" RENAME TO ""metadata"";
        ");

        migrationBuilder.CreateTable(
            name: "anilist_media_map",
            columns: table => new
            {
                primary_id = table.Column<int>(type: "INTEGER", nullable: false),
                media_type = table.Column<string>(type: "TEXT", nullable: false),
                ani_list_id = table.Column<int>(type: "INTEGER", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_anilist_media_map", x => new { x.primary_id, x.media_type });
            });

        migrationBuilder.CreateTable(
            name: "playback_resume",
            columns: table => new
            {
                video_url = table.Column<string>(type: "TEXT", nullable: false),
                position_seconds = table.Column<double>(type: "REAL", nullable: false),
                duration_seconds = table.Column<double>(type: "REAL", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_playback_resume", x => x.video_url);
            });

        migrationBuilder.CreateTable(
            name: "scrobble_progress",
            columns: table => new
            {
                key = table.Column<string>(type: "TEXT", nullable: false),
                elapsed_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scrobble_progress", x => x.key);
            });

        migrationBuilder.CreateTable(
            name: "title_mappings",
            columns: table => new
            {
                normalized_title = table.Column<string>(type: "TEXT", nullable: false),
                target_id = table.Column<int>(type: "INTEGER", nullable: false),
                media_kind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Anime"),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_title_mappings", x => x.normalized_title);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "anilist_media_map");

        migrationBuilder.DropTable(
            name: "playback_resume");

        migrationBuilder.DropTable(
            name: "scrobble_progress");

        migrationBuilder.DropTable(
            name: "title_mappings");

        migrationBuilder.Sql(@"
            CREATE TABLE ""metadata_dg_tmp"" (
                ""id"" INTEGER NOT NULL CONSTRAINT ""PK_metadata"" PRIMARY KEY AUTOINCREMENT,
                ""name"" TEXT NULL,
                ""russian"" TEXT NULL,
                ""description"" TEXT NULL,
                ""episodes"" INTEGER NULL,
                ""episodes_aired"" INTEGER NULL,
                ""next_episode_at"" TEXT NULL,
                ""fetched_at"" TEXT NOT NULL,
                ""poster_url"" TEXT NULL
            );

            INSERT OR IGNORE INTO ""metadata_dg_tmp"" (
                ""id"", ""name"", ""russian"", ""description"",
                ""episodes"", ""episodes_aired"", ""next_episode_at"", ""fetched_at"", ""poster_url""
            )
            SELECT
                CASE
                    WHEN ""media_kind"" = 'Manga' THEN (""id"" | 1073741824)
                    WHEN ""media_kind"" = 'LightNovel' THEN (""id"" | 536870912)
                    ELSE ""id""
                END,
                ""name"", ""russian"", ""description"",
                ""episodes"", ""episodes_aired"", ""next_episode_at"", ""fetched_at"", ""poster_url""
            FROM ""metadata"";

            DROP TABLE ""metadata"";

            ALTER TABLE ""metadata_dg_tmp"" RENAME TO ""metadata"";
        ");
    }
}
