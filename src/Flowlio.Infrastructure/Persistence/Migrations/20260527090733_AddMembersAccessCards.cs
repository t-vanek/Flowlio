using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembersAccessCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "FamilyMembers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "FamilyMembers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GuardianMemberId",
                table: "FamilyMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerMemberId",
                table: "BankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountAccesses_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountAccesses_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardholderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ExpiryMonth = table.Column<int>(type: "integer", nullable: false),
                    ExpiryYear = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MonthlyLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankCards_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankCards_FamilyMembers_HolderMemberId",
                        column: x => x.HolderMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FamilyInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyInvitations_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyInvitations_FamilyMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_GuardianMemberId",
                table: "FamilyMembers",
                column: "GuardianMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_OwnerMemberId",
                table: "BankAccounts",
                column: "OwnerMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountAccesses_BankAccountId_FamilyMemberId",
                table: "AccountAccesses",
                columns: new[] { "BankAccountId", "FamilyMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountAccesses_FamilyMemberId",
                table: "AccountAccesses",
                column: "FamilyMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BankCards_BankAccountId",
                table: "BankCards",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankCards_HolderMemberId",
                table: "BankCards",
                column: "HolderMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvitations_FamilyId",
                table: "FamilyInvitations",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvitations_MemberId",
                table: "FamilyInvitations",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvitations_TokenHash",
                table: "FamilyInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_FamilyMembers_OwnerMemberId",
                table: "BankAccounts",
                column: "OwnerMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyMembers_FamilyMembers_GuardianMemberId",
                table: "FamilyMembers",
                column: "GuardianMemberId",
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

            migrationBuilder.DropForeignKey(
                name: "FK_FamilyMembers_FamilyMembers_GuardianMemberId",
                table: "FamilyMembers");

            migrationBuilder.DropTable(
                name: "AccountAccesses");

            migrationBuilder.DropTable(
                name: "BankCards");

            migrationBuilder.DropTable(
                name: "FamilyInvitations");

            migrationBuilder.DropIndex(
                name: "IX_FamilyMembers_GuardianMemberId",
                table: "FamilyMembers");

            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_OwnerMemberId",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "GuardianMemberId",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "OwnerMemberId",
                table: "BankAccounts");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "FamilyMembers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
