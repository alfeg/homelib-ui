using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyHomeLibServer.Migrations
{
    public partial class AddMappedFts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
           
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "books_fts");
        }
    }
}
