using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddCardBannerColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "Shelves",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "Shelves",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "CardBannerMode",
            schema: "app",
            table: "Shelves",
            type: "INTEGER",
            nullable: false,
            defaultValue: (byte)0);

        migrationBuilder.AddColumn<string>(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "ReadingLists",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "ReadingLists",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "CardBannerMode",
            schema: "app",
            table: "ReadingLists",
            type: "INTEGER",
            nullable: false,
            defaultValue: (byte)0);

        migrationBuilder.AddColumn<string>(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "Collections",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "Collections",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "CardBannerMode",
            schema: "app",
            table: "Collections",
            type: "INTEGER",
            nullable: false,
            defaultValue: (byte)0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "Shelves");

        migrationBuilder.DropColumn(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "Shelves");

        migrationBuilder.DropColumn(
            name: "CardBannerMode",
            schema: "app",
            table: "Shelves");

        migrationBuilder.DropColumn(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "ReadingLists");

        migrationBuilder.DropColumn(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "ReadingLists");

        migrationBuilder.DropColumn(
            name: "CardBannerMode",
            schema: "app",
            table: "ReadingLists");

        migrationBuilder.DropColumn(
            name: "CardBannerBookIdsJson",
            schema: "app",
            table: "Collections");

        migrationBuilder.DropColumn(
            name: "CardBannerImageFileName",
            schema: "app",
            table: "Collections");

        migrationBuilder.DropColumn(
            name: "CardBannerMode",
            schema: "app",
            table: "Collections");
    }
}