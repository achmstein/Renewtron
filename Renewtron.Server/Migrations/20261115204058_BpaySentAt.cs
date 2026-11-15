using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class BpaySentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BpaySentAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BpaySentAt",
                table: "BusinessNameRegistrations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BpaySentAt",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpaySentAt",
                table: "BusinessNameRegistrations");
        }
    }
}
