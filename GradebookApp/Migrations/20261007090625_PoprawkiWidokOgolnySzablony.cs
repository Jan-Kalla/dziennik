using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GradebookApp.Migrations
{
    /// <inheritdoc />
    public partial class PoprawkiWidokOgolnySzablony : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    WorkType = table.Column<string>(type: "TEXT", nullable: false),
                    MaxFinalPoints = table.Column<double>(type: "REAL", nullable: false),
                    HasGroups = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTemplateTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPointsLevel1 = table.Column<double>(type: "REAL", nullable: true),
                    MaxPointsLevel2 = table.Column<double>(type: "REAL", nullable: true),
                    MaxPointsLevel3 = table.Column<double>(type: "REAL", nullable: true),
                    GroupName = table.Column<string>(type: "TEXT", nullable: true),
                    WorkTemplateId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTemplateTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTemplateTasks_WorkTemplates_WorkTemplateId",
                        column: x => x.WorkTemplateId,
                        principalTable: "WorkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTemplateTasks_WorkTemplateId",
                table: "WorkTemplateTasks",
                column: "WorkTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkTemplateTasks");

            migrationBuilder.DropTable(
                name: "WorkTemplates");
        }
    }
}
