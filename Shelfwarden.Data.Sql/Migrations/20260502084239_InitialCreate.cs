using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "app");

        migrationBuilder.CreateTable(
            name: "Authors",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Biography = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Authors", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Collections",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Collections", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Genres",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Genres", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ReadingLists",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ReadingLists", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Roles",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Roles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Series",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Series", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ServerSettings",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ServerSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Shelves",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                LastScannedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Shelves", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Tags",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Tags", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Users",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                AccessFailedCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RoleClaims",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoleClaims_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "app",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Books",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                SortTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Subtitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                Publisher = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Isbn = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                PublishedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                PageCount = table.Column<int>(type: "int", nullable: true),
                FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                FileFormat = table.Column<int>(type: "int", nullable: false),
                CoverImagePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                FileLastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastScannedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ShelfId = table.Column<int>(type: "int", nullable: false),
                SeriesId = table.Column<int>(type: "int", nullable: true),
                NumberInSeries = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Books", x => x.Id);
                table.ForeignKey(
                    name: "FK_Books_Series_SeriesId",
                    column: x => x.SeriesId,
                    principalSchema: "app",
                    principalTable: "Series",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Books_Shelves_ShelfId",
                    column: x => x.ShelfId,
                    principalSchema: "app",
                    principalTable: "Shelves",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ShelfFolders",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                ShelfId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShelfFolders", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShelfFolders_Shelves_ShelfId",
                    column: x => x.ShelfId,
                    principalSchema: "app",
                    principalTable: "Shelves",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApplicationRoleApplicationUser",
            schema: "app",
            columns: table => new
            {
                RolesId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                UsersId = table.Column<string>(type: "nvarchar(450)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApplicationRoleApplicationUser", x => new { x.RolesId, x.UsersId });
                table.ForeignKey(
                    name: "FK_ApplicationRoleApplicationUser_Roles_RolesId",
                    column: x => x.RolesId,
                    principalSchema: "app",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApplicationRoleApplicationUser_Users_UsersId",
                    column: x => x.UsersId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserClaims",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserClaims_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserLogins",
            schema: "app",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_UserLogins_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "app",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserTokens",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_UserTokens_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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
                HangfireJobId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
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

        migrationBuilder.CreateTable(
            name: "BookAuthors",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BookId = table.Column<int>(type: "int", nullable: false),
                AuthorId = table.Column<int>(type: "int", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookAuthors", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookAuthors_Authors_AuthorId",
                    column: x => x.AuthorId,
                    principalSchema: "app",
                    principalTable: "Authors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BookAuthors_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookGenres",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BookId = table.Column<int>(type: "int", nullable: false),
                GenreId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookGenres", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookGenres_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BookGenres_Genres_GenreId",
                    column: x => x.GenreId,
                    principalSchema: "app",
                    principalTable: "Genres",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Bookmarks",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                BookId = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                PageNumber = table.Column<int>(type: "int", nullable: true),
                Location = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                Note = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bookmarks", x => x.Id);
                table.ForeignKey(
                    name: "FK_Bookmarks_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookProgress",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                BookId = table.Column<int>(type: "int", nullable: false),
                Percentage = table.Column<double>(type: "float", nullable: false),
                PageNumber = table.Column<int>(type: "int", nullable: true),
                Location = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookProgress", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookProgress_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookTags",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BookId = table.Column<int>(type: "int", nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookTags", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookTags_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BookTags_Tags_TagId",
                    column: x => x.TagId,
                    principalSchema: "app",
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CollectionBooks",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CollectionId = table.Column<int>(type: "int", nullable: false),
                BookId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CollectionBooks", x => x.Id);
                table.ForeignKey(
                    name: "FK_CollectionBooks_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CollectionBooks_Collections_CollectionId",
                    column: x => x.CollectionId,
                    principalSchema: "app",
                    principalTable: "Collections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ReadingListItems",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReadingListId = table.Column<int>(type: "int", nullable: false),
                BookId = table.Column<int>(type: "int", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReadingListItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_ReadingListItems_Books_BookId",
                    column: x => x.BookId,
                    principalSchema: "app",
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ReadingListItems_ReadingLists_ReadingListId",
                    column: x => x.ReadingListId,
                    principalSchema: "app",
                    principalTable: "ReadingLists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApplicationRoleApplicationUser_UsersId",
            schema: "app",
            table: "ApplicationRoleApplicationUser",
            column: "UsersId");

        migrationBuilder.CreateIndex(
            name: "IX_Audiobooks_BookId",
            schema: "app",
            table: "Audiobooks",
            column: "BookId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Authors_NormalizedName",
            schema: "app",
            table: "Authors",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BookAuthors_AuthorId",
            schema: "app",
            table: "BookAuthors",
            column: "AuthorId");

        migrationBuilder.CreateIndex(
            name: "IX_BookAuthors_BookId_AuthorId",
            schema: "app",
            table: "BookAuthors",
            columns: new[] { "BookId", "AuthorId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BookGenres_BookId_GenreId",
            schema: "app",
            table: "BookGenres",
            columns: new[] { "BookId", "GenreId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BookGenres_GenreId",
            schema: "app",
            table: "BookGenres",
            column: "GenreId");

        migrationBuilder.CreateIndex(
            name: "IX_Bookmarks_BookId",
            schema: "app",
            table: "Bookmarks",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_Bookmarks_UserId_BookId",
            schema: "app",
            table: "Bookmarks",
            columns: new[] { "UserId", "BookId" });

        migrationBuilder.CreateIndex(
            name: "IX_BookProgress_BookId",
            schema: "app",
            table: "BookProgress",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_BookProgress_UserId_BookId",
            schema: "app",
            table: "BookProgress",
            columns: new[] { "UserId", "BookId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Books_SeriesId",
            schema: "app",
            table: "Books",
            column: "SeriesId");

        migrationBuilder.CreateIndex(
            name: "IX_Books_ShelfId_FilePath",
            schema: "app",
            table: "Books",
            columns: new[] { "ShelfId", "FilePath" });

        migrationBuilder.CreateIndex(
            name: "IX_Books_Title",
            schema: "app",
            table: "Books",
            column: "Title");

        migrationBuilder.CreateIndex(
            name: "IX_BookTags_BookId_TagId",
            schema: "app",
            table: "BookTags",
            columns: new[] { "BookId", "TagId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BookTags_TagId",
            schema: "app",
            table: "BookTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_CollectionBooks_BookId",
            schema: "app",
            table: "CollectionBooks",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_CollectionBooks_CollectionId_BookId",
            schema: "app",
            table: "CollectionBooks",
            columns: new[] { "CollectionId", "BookId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Collections_OwnerUserId_Name",
            schema: "app",
            table: "Collections",
            columns: new[] { "OwnerUserId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Genres_NormalizedName",
            schema: "app",
            table: "Genres",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReadingListItems_BookId",
            schema: "app",
            table: "ReadingListItems",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_ReadingListItems_ReadingListId_BookId",
            schema: "app",
            table: "ReadingListItems",
            columns: new[] { "ReadingListId", "BookId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReadingListItems_ReadingListId_Position",
            schema: "app",
            table: "ReadingListItems",
            columns: new[] { "ReadingListId", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ReadingLists_OwnerUserId_Name",
            schema: "app",
            table: "ReadingLists",
            columns: new[] { "OwnerUserId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RoleClaims_RoleId",
            schema: "app",
            table: "RoleClaims",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            schema: "app",
            table: "Roles",
            column: "NormalizedName",
            unique: true,
            filter: "[NormalizedName] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Series_NormalizedName",
            schema: "app",
            table: "Series",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServerSettings_Key",
            schema: "app",
            table: "ServerSettings",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShelfFolders_ShelfId",
            schema: "app",
            table: "ShelfFolders",
            column: "ShelfId");

        migrationBuilder.CreateIndex(
            name: "IX_Shelves_Name",
            schema: "app",
            table: "Shelves",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_NormalizedName",
            schema: "app",
            table: "Tags",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserClaims_UserId",
            schema: "app",
            table: "UserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserLogins_UserId",
            schema: "app",
            table: "UserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            schema: "app",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "app",
            table: "Users",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            schema: "app",
            table: "Users",
            column: "NormalizedUserName",
            unique: true,
            filter: "[NormalizedUserName] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ApplicationRoleApplicationUser",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Audiobooks",
            schema: "app");

        migrationBuilder.DropTable(
            name: "BookAuthors",
            schema: "app");

        migrationBuilder.DropTable(
            name: "BookGenres",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Bookmarks",
            schema: "app");

        migrationBuilder.DropTable(
            name: "BookProgress",
            schema: "app");

        migrationBuilder.DropTable(
            name: "BookTags",
            schema: "app");

        migrationBuilder.DropTable(
            name: "CollectionBooks",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ReadingListItems",
            schema: "app");

        migrationBuilder.DropTable(
            name: "RoleClaims",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ServerSettings",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ShelfFolders",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserClaims",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserLogins",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserRoles",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserTokens",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Authors",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Genres",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Tags",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Collections",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Books",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ReadingLists",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Roles",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Users",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Series",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Shelves",
            schema: "app");
    }
}