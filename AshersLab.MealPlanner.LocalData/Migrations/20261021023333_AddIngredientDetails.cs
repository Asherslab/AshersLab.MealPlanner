using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseUnit",
                table: "Ingredient",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                // Not "": rows from before this column must still read back as a BaseUnit.
                defaultValue: "Gram");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Ingredient",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaple",
                table: "Ingredient",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UsualStoreId",
                table: "Ingredient",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredient_CategoryId",
                table: "Ingredient",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredient_UsualStoreId",
                table: "Ingredient",
                column: "UsualStoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ingredient_CategoryId",
                table: "Ingredient");

            migrationBuilder.DropIndex(
                name: "IX_Ingredient_UsualStoreId",
                table: "Ingredient");

            migrationBuilder.DropColumn(
                name: "BaseUnit",
                table: "Ingredient");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Ingredient");

            migrationBuilder.DropColumn(
                name: "IsStaple",
                table: "Ingredient");

            migrationBuilder.DropColumn(
                name: "UsualStoreId",
                table: "Ingredient");
        }
    }
}
