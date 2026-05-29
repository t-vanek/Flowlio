using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AspspName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AspspCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AuthorizationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AccountUid = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConsentValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnections", x => x.Id);
                    table.CheckConstraint("CK_BankConnection_Country", "char_length(\"AspspCountry\") = 2");
                    table.ForeignKey(
                        name: "FK_BankConnections_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankConnections_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_BankAccountId",
                table: "BankConnections",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_FamilyId",
                table: "BankConnections",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_State",
                table: "BankConnections",
                column: "State",
                filter: "\"State\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankConnections");
        }
    }
}
