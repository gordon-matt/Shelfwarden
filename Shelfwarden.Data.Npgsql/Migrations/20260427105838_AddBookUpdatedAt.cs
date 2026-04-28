using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations;

/// <inheritdoc />
public partial class AddBookUpdatedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            schema: "app",
            table: "Books",
            type: "timestamp with time zone",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "UpdatedAt",
            schema: "app",
            table: "Books");
}