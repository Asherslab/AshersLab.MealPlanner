using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanRotationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Earlier",
                table: "PlanTemplate",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Earlier",
                table: "PlanTemplate");
        }
    }
}
