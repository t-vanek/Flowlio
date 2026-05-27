using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimisticConcurrency : Migration
    {
        // Family, FamilyMember, BankCard and AccountAccess use the Postgres "xmin" system column as an
        // optimistic-concurrency token (mapped as a uint row-version in the model). xmin already exists on
        // every table, so there is nothing to create — this migration only records the model change.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
