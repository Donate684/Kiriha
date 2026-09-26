using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserStateTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hidden_seasonal_anime",
                columns: table => new
                {
                    anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hidden_seasonal_anime", x => x.anime_id);
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
                    table.PrimaryKey("pk_hidden_torrent_anime", x => x.anime_id);
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
                    table.PrimaryKey("pk_torrent_title_filters", x => x.anime_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "torrent_title_filters");

            migrationBuilder.DropTable(
                name: "hidden_torrent_anime");

            migrationBuilder.DropTable(
                name: "hidden_seasonal_anime");
        }
    }
}
