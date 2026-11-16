using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Data.Migrations;

/// <inheritdoc />
public partial class _20261116105000_CoreSchemaCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "episode_list_meta");

        migrationBuilder.DropTable(
            name: "episode_releases");

        migrationBuilder.DropTable(
            name: "file_recognition_cache");

        migrationBuilder.AddColumn<string>(
            name: "poster_url",
            table: "metadata",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "media_kind",
            table: "history",
            type: "TEXT",
            nullable: false,
            defaultValue: "Anime");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "poster_url",
            table: "metadata");

        migrationBuilder.DropColumn(
            name: "media_kind",
            table: "history");

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
                air_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                episode_number = table.Column<int>(type: "INTEGER", nullable: false),
                mal_id = table.Column<int>(type: "INTEGER", nullable: false),
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

        migrationBuilder.CreateIndex(
            name: "idx_episode_releases_mal_id",
            table: "episode_releases",
            column: "mal_id");
    }
}
