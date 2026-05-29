using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCategorySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing transactions predate CategorySource tracking, so they default to None (0). We can't
            // tell after the fact which were set by hand versus by a rule, so treat every already-categorized
            // row as rule-assigned: this preserves the prior behaviour (a later "recategorize all" may still
            // update them) and avoids flooding the new suggestion panel with historical merchants. Manual
            // protection and learning then apply to categorizations made from here on.
            migrationBuilder.Sql(
                @"UPDATE ""Transactions"" SET ""CategorySource"" = 1
                  WHERE ""CategoryId"" IS NOT NULL AND ""CategorySource"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert the backfilled rows to None.
            migrationBuilder.Sql(
                @"UPDATE ""Transactions"" SET ""CategorySource"" = 0
                  WHERE ""CategoryId"" IS NOT NULL AND ""CategorySource"" = 1;");
        }
    }
}
