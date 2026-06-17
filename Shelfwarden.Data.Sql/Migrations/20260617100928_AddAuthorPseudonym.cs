using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorPseudonym : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrimaryAuthorId",
                schema: "app",
                table: "Authors",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Authors_PrimaryAuthorId",
                schema: "app",
                table: "Authors",
                column: "PrimaryAuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Authors_Authors_PrimaryAuthorId",
                schema: "app",
                table: "Authors",
                column: "PrimaryAuthorId",
                principalSchema: "app",
                principalTable: "Authors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Authors_Authors_PrimaryAuthorId",
                schema: "app",
                table: "Authors");

            migrationBuilder.DropIndex(
                name: "IX_Authors_PrimaryAuthorId",
                schema: "app",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "PrimaryAuthorId",
                schema: "app",
                table: "Authors");
        }
    }
}
