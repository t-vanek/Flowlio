using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Diacritics folding for full-text search. unaccent itself is only STABLE, so wrap it in
            // an IMMUTABLE function (pinning the dictionary) — required to use it in the generated
            // SearchVector column and its GIN index below.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION flowlio_immutable_unaccent(text)
                RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $func$ SELECT unaccent('unaccent', $1) $func$;");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Transactions",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', flowlio_immutable_unaccent(coalesce(\"CounterpartyName\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"Note\", '') || ' ' || coalesce(\"CounterpartyAccount\", '') || ' ' || coalesce(\"VariableSymbol\", '')))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SearchVector",
                table: "Transactions",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_SearchVector",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Transactions");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS flowlio_immutable_unaccent(text);");
        }
    }
}
