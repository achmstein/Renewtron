using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorRenewalRequestPaymentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns
            migrationBuilder.AddColumn<int>(
                name: "PaymentType",
                table: "RenewalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0); // Stripe

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPaymentReference",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Migrate existing data
            // Set PaymentType based on IsManualPayment
            migrationBuilder.Sql(@"
                UPDATE RenewalRequests
                SET PaymentType = CASE WHEN IsManualPayment = 1 THEN 1 ELSE 0 END
            ");

            // Copy CustomerAmount to Amount
            migrationBuilder.Sql(@"
                UPDATE RenewalRequests
                SET Amount = CustomerAmount
            ");

            // Drop old columns
            migrationBuilder.DropColumn(
                name: "CustomerAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AsicAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "IsManualPayment",
                table: "RenewalRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add back old columns
            migrationBuilder.AddColumn<decimal>(
                name: "CustomerAmount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AsicAmount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsManualPayment",
                table: "RenewalRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Migrate data back
            migrationBuilder.Sql(@"
                UPDATE RenewalRequests
                SET CustomerAmount = Amount,
                    AsicAmount = Amount,
                    IsManualPayment = CASE WHEN PaymentType = 1 THEN 1 ELSE 0 END
            ");

            // Drop new columns
            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "ExternalPaymentReference",
                table: "RenewalRequests");
        }
    }
}
