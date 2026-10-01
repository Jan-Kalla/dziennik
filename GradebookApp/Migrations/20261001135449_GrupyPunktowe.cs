using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GradebookApp.Migrations
{
    /// <inheritdoc />
    public partial class GrupyPunktowe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "WrittenWorkTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasGroups",
                table: "WrittenWorks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupName",
                table: "WrittenWorkTasks");

            migrationBuilder.DropColumn(
                name: "HasGroups",
                table: "WrittenWorks");
        }
    }
}
