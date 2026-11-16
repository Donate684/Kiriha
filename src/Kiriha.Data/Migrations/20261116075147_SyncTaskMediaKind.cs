using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Data.Migrations;

/// <inheritdoc />
public partial class _20261116075147_SyncTaskMediaKind : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "media_kind",
            table: "sync_tasks",
            type: "TEXT",
            nullable: false,
            defaultValue: "Anime");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "media_kind",
            table: "sync_tasks");
    }
}
