using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteAndFamilyCascades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CategorizationRules_Families_FamilyId",
                table: "CategorizationRules",
                column: "FamilyId",
                principalTable: "Families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringPayments_Families_FamilyId",
                table: "RecurringPayments",
                column: "FamilyId",
                principalTable: "Families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Families_FamilyId",
                table: "Subscriptions",
                column: "FamilyId",
                principalTable: "Families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CategorizationRules_Families_FamilyId",
                table: "CategorizationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringPayments_Families_FamilyId",
                table: "RecurringPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Families_FamilyId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "AspNetUsers");
        }
    }
}
