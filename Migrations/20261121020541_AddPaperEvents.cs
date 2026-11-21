using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iPrinterTool.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUser = table.Column<string>(type: "TEXT", nullable: false),
                    LastFile = table.Column<string>(type: "TEXT", nullable: false),
                    LastPages = table.Column<int>(type: "INTEGER", nullable: false),
                    LastStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TopUser3h = table.Column<string>(type: "TEXT", nullable: false),
                    TopPages3h = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperEvents", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperEvents");
        }
    }
}
