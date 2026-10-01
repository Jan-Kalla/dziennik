using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GradebookApp.Migrations
{
    /// <inheritdoc />
    public partial class DodanieKalendarza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TWORZYMY TYLKO NOWE TABELE KALENDARZA, IGNORUJEMY STARE (Klasy, Uczniowie itp.)
            migrationBuilder.CreateTable(
                name: "PlannedRetakes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WrittenWorkId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedRetakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedRetakes_WrittenWorks_WrittenWorkId",
                        column: x => x.WrittenWorkId,
                        principalTable: "WrittenWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlannedRetakeStudents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlannedRetakeId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedRetakeStudents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedRetakeStudents_PlannedRetakes_PlannedRetakeId",
                        column: x => x.PlannedRetakeId,
                        principalTable: "PlannedRetakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedRetakeStudents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedRetakes_WrittenWorkId",
                table: "PlannedRetakes",
                column: "WrittenWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedRetakeStudents_PlannedRetakeId",
                table: "PlannedRetakeStudents",
                column: "PlannedRetakeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedRetakeStudents_StudentId",
                table: "PlannedRetakeStudents",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannedRetakeStudents");

            migrationBuilder.DropTable(
                name: "StudentTaskScores");

            migrationBuilder.DropTable(
                name: "StudentWorkRecords");

            migrationBuilder.DropTable(
                name: "StudentWrittenWork");

            migrationBuilder.DropTable(
                name: "PlannedRetakes");

            migrationBuilder.DropTable(
                name: "WrittenWorkTasks");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropTable(
                name: "WrittenWorks");

            migrationBuilder.DropTable(
                name: "Classes");
        }
    }
}
