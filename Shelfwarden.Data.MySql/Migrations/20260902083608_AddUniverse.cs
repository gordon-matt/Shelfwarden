using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_OwnerUserId_Name",
                table: "ReadingLists");

            migrationBuilder.AddColumn<int>(
                name: "UniverseId",
                table: "Series",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UniverseId",
                table: "ReadingLists",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Universes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Universes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UniverseBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UniverseId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    TimelineDate = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    TimelineOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniverseBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UniverseBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UniverseBooks_Universes_UniverseId",
                        column: x => x.UniverseId,
                        principalTable: "Universes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Series_UniverseId",
                table: "Series",
                column: "UniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_OwnerUserId_UniverseId_Name",
                table: "ReadingLists",
                columns: new[] { "OwnerUserId", "UniverseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_UniverseId",
                table: "ReadingLists",
                column: "UniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_BookId",
                table: "UniverseBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_BookId",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "TimelineOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Universes_NormalizedName",
                table: "Universes",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingLists_Universes_UniverseId",
                table: "ReadingLists",
                column: "UniverseId",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series",
                column: "UniverseId",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingLists_Universes_UniverseId",
                table: "ReadingLists");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series");

            migrationBuilder.DropTable(
                name: "UniverseBooks");

            migrationBuilder.DropTable(
                name: "Universes");

            migrationBuilder.DropIndex(
                name: "IX_Series_UniverseId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_OwnerUserId_UniverseId_Name",
                table: "ReadingLists");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_UniverseId",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "UniverseId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "UniverseId",
                table: "ReadingLists");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_OwnerUserId_Name",
                table: "ReadingLists",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);
        }
    }
}
