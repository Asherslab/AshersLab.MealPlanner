using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AddReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoversItemId",
                table: "StockMovement",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductAlias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAlias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Receipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PurchasedOn = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SubtotalCents = table.Column<long>(type: "INTEGER", nullable: false),
                    FeesCents = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCents = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidCents = table.Column<long>(type: "INTEGER", nullable: true),
                    RefundCents = table.Column<long>(type: "INTEGER", nullable: true),
                    ImportedOn = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StoreCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitPriceCents = table.Column<long>(type: "INTEGER", nullable: true),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    WeightGrams = table.Column<decimal>(type: "TEXT", nullable: true),
                    PerKgCents = table.Column<long>(type: "INTEGER", nullable: true),
                    DiscountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptLine", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAlias_ProductId",
                table: "ProductAlias",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAlias_StoreId",
                table: "ProductAlias",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLine_ProductId",
                table: "ReceiptLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLine_ReceiptId",
                table: "ReceiptLine",
                column: "ReceiptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductAlias");

            migrationBuilder.DropTable(
                name: "Receipt");

            migrationBuilder.DropTable(
                name: "ReceiptLine");

            migrationBuilder.DropColumn(
                name: "CoversItemId",
                table: "StockMovement");
        }
    }
}
