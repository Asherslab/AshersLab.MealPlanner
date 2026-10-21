using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StartsOn",
                table: "PlanTemplate",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalendarEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MealId = table.Column<Guid>(type: "TEXT", nullable: true),
                    People = table.Column<string>(type: "TEXT", maxLength: 740, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    MovedDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    MovedSlotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntry_Date",
                table: "CalendarEntry",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntry_MovedDate",
                table: "CalendarEntry",
                column: "MovedDate");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntry_TemplateEntryId",
                table: "CalendarEntry",
                column: "TemplateEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEntry");

            migrationBuilder.DropColumn(
                name: "StartsOn",
                table: "PlanTemplate");
        }
    }
}
