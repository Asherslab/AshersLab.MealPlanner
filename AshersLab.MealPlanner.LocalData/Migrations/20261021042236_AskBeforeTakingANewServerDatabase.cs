using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.LocalData.Migrations
{
    /// <inheritdoc />
    public partial class AskBeforeTakingANewServerDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByEpoch",
                table: "SyncState",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacedMissingRows",
                table: "SyncState",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SendCopyOnNextSnapshot",
                table: "SyncState",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacedByEpoch",
                table: "SyncState");

            migrationBuilder.DropColumn(
                name: "ReplacedMissingRows",
                table: "SyncState");

            migrationBuilder.DropColumn(
                name: "SendCopyOnNextSnapshot",
                table: "SyncState");
        }
    }
}
