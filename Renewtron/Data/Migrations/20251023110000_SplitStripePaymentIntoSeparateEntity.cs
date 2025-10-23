using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitStripePaymentIntoSeparateEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create StripePayments table
            migrationBuilder.CreateTable(
                name: "StripePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RenewalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripePayments_RenewalRequests_RenewalRequestId",
                        column: x => x.RenewalRequestId,
                        principalTable: "RenewalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripePayments_RenewalRequestId",
                table: "StripePayments",
                column: "RenewalRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePayments_PaymentIntentId",
                table: "StripePayments",
                column: "PaymentIntentId");

            // Migrate existing Stripe payment data to StripePayments table
            migrationBuilder.Sql(@"
                INSERT INTO StripePayments (Id, RenewalRequestId, PaymentIntentId, PaymentStatus, PaidAt)
                SELECT
                    NEWID(),
                    Id,
                    COALESCE(StripePaymentIntentId, ''),
                    COALESCE(StripePaymentStatus, ''),
                    StripePaidAt
                FROM RenewalRequests
                WHERE PaymentType = 0 AND StripePaymentIntentId IS NOT NULL
            ");

            // Drop old columns from RenewalRequests
            migrationBuilder.DropColumn(
                name: "ExternalPaymentReference",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaymentStatus",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaidAt",
                table: "RenewalRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add back old columns to RenewalRequests
            migrationBuilder.AddColumn<string>(
                name: "ExternalPaymentReference",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePaymentIntentId",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePaymentStatus",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StripePaidAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);

            // Migrate data back from StripePayments to RenewalRequests
            migrationBuilder.Sql(@"
                UPDATE rr
                SET
                    rr.StripePaymentIntentId = sp.PaymentIntentId,
                    rr.StripePaymentStatus = sp.PaymentStatus,
                    rr.StripePaidAt = sp.PaidAt
                FROM RenewalRequests rr
                INNER JOIN StripePayments sp ON rr.Id = sp.RenewalRequestId
            ");

            // Drop StripePayments table
            migrationBuilder.DropTable(
                name: "StripePayments");
        }
    }
}
