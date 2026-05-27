using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerMemberId",
                table: "BankAccounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_OwnerMemberId",
                table: "BankAccounts",
                column: "OwnerMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_FamilyMembers_OwnerMemberId",
                table: "BankAccounts",
                column: "OwnerMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankAccounts_FamilyMembers_OwnerMemberId",
                table: "BankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_OwnerMemberId",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "OwnerMemberId",
                table: "BankAccounts");
        }
    }
}
