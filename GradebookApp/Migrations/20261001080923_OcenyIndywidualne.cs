using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GradebookApp.Migrations
{
    /// <inheritdoc />
    public partial class OcenyIndywidualne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IndividualStudentId",
                table: "WrittenWorks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIndividual",
                table: "WrittenWorks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndividualStudentId",
                table: "WrittenWorks");

            migrationBuilder.DropColumn(
                name: "IsIndividual",
                table: "WrittenWorks");
        }
    }
}
