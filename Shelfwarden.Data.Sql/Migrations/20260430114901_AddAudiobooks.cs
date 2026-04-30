using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Audiobooks",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VoiceName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutputFileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TotalChunks = table.Column<int>(type: "int", nullable: false),
                    CompletedChunks = table.Column<int>(type: "int", nullable: false),
                    OutputSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audiobooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Audiobooks_Books_BookId",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audiobooks_BookId",
                schema: "app",
                table: "Audiobooks",
                column: "BookId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Audiobooks",
                schema: "app");
        }
    }
}
