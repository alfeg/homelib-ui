using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyHomeLibServer.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Authors = table.Column<string>(type: "TEXT", nullable: false),
                    Genre = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Series = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesNo = table.Column<string>(type: "TEXT", nullable: false),
                    LibId = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchiveFile = table.Column<string>(type: "TEXT", nullable: false),
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    Ext = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Lang = table.Column<string>(type: "TEXT", nullable: false),
                    Deleted = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<string>(type: "TEXT", nullable: false),
                    LibRate = table.Column<string>(type: "TEXT", nullable: false),
                    Keywords = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_books", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_books_Authors",
                table: "books",
                column: "Authors");

            migrationBuilder.CreateIndex(
                name: "IX_books_Genre",
                table: "books",
                column: "Genre");

            migrationBuilder.CreateIndex(
                name: "IX_books_Keywords",
                table: "books",
                column: "Keywords");

            migrationBuilder.CreateIndex(
                name: "IX_books_Series",
                table: "books",
                column: "Series");

            migrationBuilder.CreateIndex(
                name: "IX_books_Title",
                table: "books",
                column: "Title");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "books");
        }
    }
}
