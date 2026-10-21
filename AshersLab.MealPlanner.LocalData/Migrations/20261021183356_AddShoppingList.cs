using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShoppingItemId",
                table: "StockMovement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShoppingList",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartsOn = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EndsOn = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingListItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngredientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PriceCents = table.Column<long>(type: "INTEGER", nullable: true),
                    Tick = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingListItem", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListItem_ListId",
                table: "ShoppingListItem",
                column: "ListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShoppingList");

            migrationBuilder.DropTable(
                name: "ShoppingListItem");

            migrationBuilder.DropColumn(
                name: "ShoppingItemId",
                table: "StockMovement");
        }
    }
}
