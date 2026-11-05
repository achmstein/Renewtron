using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class HandledOutsideRenewtron : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HandledAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandledNote",
                table: "RenewalRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandledAt",
                table: "OntraportSales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandledNote",
                table: "OntraportSales",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandledAt",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "HandledNote",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "HandledAt",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "HandledNote",
                table: "OntraportSales");
        }
    }
}
