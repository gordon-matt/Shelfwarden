using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_OwnerUserId_Name",
                schema: "app",
                table: "ReadingLists");

            migrationBuilder.AddColumn<int>(
                name: "UniverseId",
                schema: "app",
                table: "Series",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UniverseId",
                schema: "app",
                table: "ReadingLists",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Universes",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Universes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UniverseBooks",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniverseId = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    TimelineDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TimelineOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniverseBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UniverseBooks_Books_BookId",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UniverseBooks_Universes_UniverseId",
                        column: x => x.UniverseId,
                        principalSchema: "app",
                        principalTable: "Universes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Series_UniverseId",
                schema: "app",
                table: "Series",
                column: "UniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_OwnerUserId_UniverseId_Name",
                schema: "app",
                table: "ReadingLists",
                columns: new[] { "OwnerUserId", "UniverseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_UniverseId",
                schema: "app",
                table: "ReadingLists",
                column: "UniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_BookId",
                schema: "app",
                table: "UniverseBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_BookId",
                schema: "app",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                schema: "app",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "TimelineOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Universes_NormalizedName",
                schema: "app",
                table: "Universes",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingLists_Universes_UniverseId",
                schema: "app",
                table: "ReadingLists",
                column: "UniverseId",
                principalSchema: "app",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_Universes_UniverseId",
                schema: "app",
                table: "Series",
                column: "UniverseId",
                principalSchema: "app",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingLists_Universes_UniverseId",
                schema: "app",
                table: "ReadingLists");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_Universes_UniverseId",
                schema: "app",
                table: "Series");

            migrationBuilder.DropTable(
                name: "UniverseBooks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Universes",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_Series_UniverseId",
                schema: "app",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_OwnerUserId_UniverseId_Name",
                schema: "app",
                table: "ReadingLists");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_UniverseId",
                schema: "app",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "UniverseId",
                schema: "app",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "UniverseId",
                schema: "app",
                table: "ReadingLists");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_OwnerUserId_Name",
                schema: "app",
                table: "ReadingLists",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);
        }
    }
}
