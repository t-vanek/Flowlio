using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowlio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequire2faBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Require2faBy",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Require2faBy",
                table: "AspNetUsers");
        }
    }
}
