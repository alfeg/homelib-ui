using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyHomeLibServer.Migrations
{
    public partial class fts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
            drop table if exists books_fts;
            CREATE VIRTUAL TABLE books_fts USING fts5(id, title, authors, keywords);
            insert into books_fts select id, title , authors ,keywords from books
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
