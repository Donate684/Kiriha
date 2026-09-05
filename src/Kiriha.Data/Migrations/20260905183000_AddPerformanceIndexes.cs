using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_history_timestamp",
                table: "history",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_history_anime_id",
                table: "history",
                column: "anime_id");

            migrationBuilder.CreateIndex(
                name: "idx_user_anime_kind_status",
                table: "user_anime",
                columns: new[] { "media_kind", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_sync_tasks_anime_id",
                table: "sync_tasks",
                column: "anime_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_history_timestamp",
                table: "history");

            migrationBuilder.DropIndex(
                name: "idx_history_anime_id",
                table: "history");

            migrationBuilder.DropIndex(
                name: "idx_user_anime_kind_status",
                table: "user_anime");

            migrationBuilder.DropIndex(
                name: "idx_sync_tasks_anime_id",
                table: "sync_tasks");
        }
    }
}
