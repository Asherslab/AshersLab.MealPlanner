using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.Grpc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleSlotsAndPlanTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Person",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Colour = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    WeekCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Slot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Position = table.Column<decimal>(type: "TEXT", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateChoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateChoice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Week = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    People = table.Column<string>(type: "TEXT", maxLength: 740, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateChoice_EntryId",
                table: "TemplateChoice",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateChoice_MealId",
                table: "TemplateChoice",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateEntry_SlotId",
                table: "TemplateEntry",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateEntry_TemplateId",
                table: "TemplateEntry",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Person");

            migrationBuilder.DropTable(
                name: "PlanTemplate");

            migrationBuilder.DropTable(
                name: "Slot");

            migrationBuilder.DropTable(
                name: "TemplateChoice");

            migrationBuilder.DropTable(
                name: "TemplateEntry");
        }
    }
}
