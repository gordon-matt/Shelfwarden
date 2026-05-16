using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAdditionalContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdditionalContent",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuthorId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalContent_Authors_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "app",
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BookAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAdditionalContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAdditionalContent_AdditionalContent_AdditionalContentItemId",
                        column: x => x.AdditionalContentItemId,
                        principalSchema: "app",
                        principalTable: "AdditionalContent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookAdditionalContent_Books_BookId",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAdditionalContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesAdditionalContent_AdditionalContent_AdditionalContentItemId",
                        column: x => x.AdditionalContentItemId,
                        principalSchema: "app",
                        principalTable: "AdditionalContent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesAdditionalContent_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "app",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContent_AuthorId",
                schema: "app",
                table: "AdditionalContent",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContent_FilePath",
                schema: "app",
                table: "AdditionalContent",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "BookAdditionalContent",
                column: "AdditionalContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BookAdditionalContent_BookId_AdditionalContentItemId",
                schema: "app",
                table: "BookAdditionalContent",
                columns: new[] { "BookId", "AdditionalContentItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "SeriesAdditionalContent",
                column: "AdditionalContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAdditionalContent_SeriesId_AdditionalContentItemId",
                schema: "app",
                table: "SeriesAdditionalContent",
                columns: new[] { "SeriesId", "AdditionalContentItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "SeriesAdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "AdditionalContent",
                schema: "app");
        }
    }
}
