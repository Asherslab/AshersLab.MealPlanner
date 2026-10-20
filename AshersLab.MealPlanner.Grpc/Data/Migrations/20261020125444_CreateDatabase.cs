using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AshersLab.MealPlanner.Grpc.Data.Migrations
{
    /// <summary>
    /// Empty on purpose: the model has no entities yet. Applying it creates the database file and its
    /// __EFMigrationsHistory row, which is what the scaffold's ServerInfo call reads back to prove the
    /// browser reached the database. Squash it into the first real migration freely - nothing is in
    /// production yet.
    /// </summary>
    public partial class CreateDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
