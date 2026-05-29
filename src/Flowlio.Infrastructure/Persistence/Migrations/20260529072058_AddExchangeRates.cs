using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CzkPerUnit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRate_Currency", "char_length(\"Currency\") = 3");
                    table.CheckConstraint("CK_ExchangeRate_CzkPerUnit", "\"CzkPerUnit\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_Currency_Date",
                table: "ExchangeRates",
                columns: new[] { "Currency", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");
        }
    }
}
