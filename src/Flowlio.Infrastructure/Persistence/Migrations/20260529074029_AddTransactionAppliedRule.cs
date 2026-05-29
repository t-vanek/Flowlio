using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAppliedRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppliedRuleId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AppliedRuleId",
                table: "Transactions",
                column: "AppliedRuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_CategorizationRules_AppliedRuleId",
                table: "Transactions",
                column: "AppliedRuleId",
                principalTable: "CategorizationRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_CategorizationRules_AppliedRuleId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AppliedRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AppliedRuleId",
                table: "Transactions");
        }
    }
}
