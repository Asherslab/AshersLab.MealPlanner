using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddPantry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StapleStock",
                table: "Ingredient",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                // Every ingredient already here is a staple the household has, until someone says otherwise. Not "":
                // the column is read as a StapleStock by name.
                defaultValue: "Have");

            migrationBuilder.CreateTable(
                name: "StockMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngredientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovement", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_IngredientId",
                table: "StockMovement",
                column: "IngredientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovement");

            migrationBuilder.DropColumn(
                name: "StapleStock",
                table: "Ingredient");
        }
    }
}
