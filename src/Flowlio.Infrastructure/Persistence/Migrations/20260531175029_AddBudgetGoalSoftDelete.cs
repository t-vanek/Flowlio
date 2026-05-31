using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetGoalSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Budgets and goals now use the Postgres "xmin" system column as an optimistic-concurrency
            // token (a uint row-version in the model). xmin already exists on every table, so EF's
            // scaffolded AddColumn("xmin")/DropColumn("xmin") are intentionally removed; this migration only
            // adds the soft-delete column and narrows the budget uniqueness to live rows.
            migrationBuilder.DropIndex(
                name: "IX_Budgets_FamilyId_CategoryId",
                table: "Budgets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Goals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Budgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_FamilyId_CategoryId",
                table: "Budgets",
                columns: new[] { "FamilyId", "CategoryId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Budgets_FamilyId_CategoryId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Budgets");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_FamilyId_CategoryId",
                table: "Budgets",
                columns: new[] { "FamilyId", "CategoryId" },
                unique: true);
        }
    }
}
