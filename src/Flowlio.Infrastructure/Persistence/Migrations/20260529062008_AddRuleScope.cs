using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "CategorizationRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerMemberId",
                table: "CategorizationRules",
                type: "uuid",
                nullable: true);

            // Existing rules predate scoping and applied to the whole family, so backfill them as Family (2)
            // — not the CLR default Personal (0), which (with a null owner) would stop them categorizing.
            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "CategorizationRules",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationRules_BankAccountId",
                table: "CategorizationRules",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationRules_OwnerMemberId",
                table: "CategorizationRules",
                column: "OwnerMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_CategorizationRules_BankAccounts_BankAccountId",
                table: "CategorizationRules",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CategorizationRules_FamilyMembers_OwnerMemberId",
                table: "CategorizationRules",
                column: "OwnerMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CategorizationRules_BankAccounts_BankAccountId",
                table: "CategorizationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_CategorizationRules_FamilyMembers_OwnerMemberId",
                table: "CategorizationRules");

            migrationBuilder.DropIndex(
                name: "IX_CategorizationRules_BankAccountId",
                table: "CategorizationRules");

            migrationBuilder.DropIndex(
                name: "IX_CategorizationRules_OwnerMemberId",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "OwnerMemberId",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "CategorizationRules");
        }
    }
}
