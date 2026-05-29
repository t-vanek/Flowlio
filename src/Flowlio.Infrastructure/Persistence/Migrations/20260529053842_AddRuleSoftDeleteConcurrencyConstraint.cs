using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleSoftDeleteConcurrencyConstraint : Migration
    {
        // Rules gain soft-delete (DeletedAt), optimistic concurrency (the Postgres xmin system column) and a
        // non-empty-pattern check. xmin already exists on every table, so the scaffolded AddColumn/DropColumn
        // for it is removed — that change is model-only, matching AddOptimisticConcurrency/AddTransactionConcurrency.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "CategorizationRules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules",
                sql: "char_length(btrim(\"Pattern\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CategorizationRules");
        }
    }
}
