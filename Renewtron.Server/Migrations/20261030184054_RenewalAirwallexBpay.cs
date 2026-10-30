using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class RenewalAirwallexBpay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AirwallexTransferId",
                table: "RenewalRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AirwallexTransferStatus",
                table: "RenewalRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BpayAmount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BpayPaymentAttempts",
                table: "RenewalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BpayPaymentError",
                table: "RenewalRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AirwallexTransferId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AirwallexTransferStatus",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpayAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpayPaymentAttempts",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "BpayPaymentError",
                table: "RenewalRequests");
        }
    }
}
