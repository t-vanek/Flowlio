using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Transaction_Currency",
                table: "Transactions",
                sql: "char_length(\"Currency\") = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subscription_Amount",
                table: "Subscriptions",
                sql: "\"Amount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringPayment_DayOfMonth",
                table: "RecurringPayments",
                sql: "\"DayOfMonth\" IS NULL OR \"DayOfMonth\" BETWEEN 1 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringPayment_ExpectedAmount",
                table: "RecurringPayments",
                sql: "\"ExpectedAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Family_BaseCurrency",
                table: "Families",
                sql: "char_length(\"BaseCurrency\") = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Family_Name",
                table: "Families",
                sql: "char_length(btrim(\"Name\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankCard_ExpiryMonth",
                table: "BankCards",
                sql: "\"ExpiryMonth\" BETWEEN 1 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankCard_ExpiryYear",
                table: "BankCards",
                sql: "\"ExpiryYear\" BETWEEN 2000 AND 2100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankCard_Last4",
                table: "BankCards",
                sql: "\"Last4\" IS NULL OR \"Last4\" ~ '^[0-9]{1,4}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankCard_MonthlyLimit",
                table: "BankCards",
                sql: "\"MonthlyLimit\" IS NULL OR \"MonthlyLimit\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankAccount_Currency",
                table: "BankAccounts",
                sql: "char_length(\"Currency\") = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankAccount_Name",
                table: "BankAccounts",
                sql: "char_length(btrim(\"Name\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transaction_Currency",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Subscription_Amount",
                table: "Subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringPayment_DayOfMonth",
                table: "RecurringPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringPayment_ExpectedAmount",
                table: "RecurringPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Family_BaseCurrency",
                table: "Families");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Family_Name",
                table: "Families");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankCard_ExpiryMonth",
                table: "BankCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankCard_ExpiryYear",
                table: "BankCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankCard_Last4",
                table: "BankCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankCard_MonthlyLimit",
                table: "BankCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankAccount_Currency",
                table: "BankAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankAccount_Name",
                table: "BankAccounts");
        }
    }
}
