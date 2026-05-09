using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddShelfAccessTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShelfRoleAccess",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ShelfId = table.Column<int>(type: "INTEGER", nullable: false),
                NormalizedRoleName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShelfRoleAccess", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShelfRoleAccess_Shelves_ShelfId",
                    column: x => x.ShelfId,
                    principalSchema: "app",
                    principalTable: "Shelves",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ShelfUserAccess",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ShelfId = table.Column<int>(type: "INTEGER", nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShelfUserAccess", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShelfUserAccess_Shelves_ShelfId",
                    column: x => x.ShelfId,
                    principalSchema: "app",
                    principalTable: "Shelves",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShelfRoleAccess_ShelfId_NormalizedRoleName",
            schema: "app",
            table: "ShelfRoleAccess",
            columns: new[] { "ShelfId", "NormalizedRoleName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShelfUserAccess_ShelfId_UserId",
            schema: "app",
            table: "ShelfUserAccess",
            columns: new[] { "ShelfId", "UserId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ShelfRoleAccess",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ShelfUserAccess",
            schema: "app");
    }
}