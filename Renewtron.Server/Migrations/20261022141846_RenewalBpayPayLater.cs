using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class RenewalBpayPayLater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BpayBillerCode",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BpayPaidAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BpayReference",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BpayBillerCode",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpayPaidAt",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpayReference",
                table: "RenewalRequests");
        }
    }
}
