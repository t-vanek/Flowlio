using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleAmountConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules");

            migrationBuilder.AlterColumn<string>(
                name: "Pattern",
                table: "CategorizationRules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "AmountCurrency",
                table: "CategorizationRules",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAmount",
                table: "CategorizationRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinAmount",
                table: "CategorizationRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CategorizationRule_AmountCurrency",
                table: "CategorizationRules",
                sql: "\"AmountCurrency\" IS NULL OR char_length(\"AmountCurrency\") = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CategorizationRule_Amounts",
                table: "CategorizationRules",
                sql: "(\"MinAmount\" IS NULL OR \"MinAmount\" >= 0) AND (\"MaxAmount\" IS NULL OR \"MaxAmount\" >= 0) AND (\"MinAmount\" IS NULL OR \"MaxAmount\" IS NULL OR \"MinAmount\" <= \"MaxAmount\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules",
                sql: "\"Pattern\" IS NULL OR char_length(btrim(\"Pattern\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CategorizationRule_AmountCurrency",
                table: "CategorizationRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CategorizationRule_Amounts",
                table: "CategorizationRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "AmountCurrency",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "MaxAmount",
                table: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "MinAmount",
                table: "CategorizationRules");

            migrationBuilder.AlterColumn<string>(
                name: "Pattern",
                table: "CategorizationRules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CategorizationRule_Pattern",
                table: "CategorizationRules",
                sql: "char_length(btrim(\"Pattern\")) > 0");
        }
    }
}
