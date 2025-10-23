using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveCustomerCreditCardToStripePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old foreign key from RenewalRequests
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_CustomerCreditCardId",
                table: "RenewalRequests");

            // Drop the old index from RenewalRequests
            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_CustomerCreditCardId",
                table: "RenewalRequests");

            // Add CustomerCreditCardId to StripePayments
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerCreditCardId",
                table: "StripePayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty);

            // Migrate existing data from RenewalRequests to StripePayments
            migrationBuilder.Sql(@"
                UPDATE sp
                SET sp.CustomerCreditCardId = rr.CustomerCreditCardId
                FROM StripePayments sp
                INNER JOIN RenewalRequests rr ON sp.RenewalRequestId = rr.Id
                WHERE rr.CustomerCreditCardId IS NOT NULL
            ");

            // Drop CustomerCreditCardId from RenewalRequests
            migrationBuilder.DropColumn(
                name: "CustomerCreditCardId",
                table: "RenewalRequests");

            // Create index on StripePayments.CustomerCreditCardId
            migrationBuilder.CreateIndex(
                name: "IX_StripePayments_CustomerCreditCardId",
                table: "StripePayments",
                column: "CustomerCreditCardId");

            // Add foreign key to StripePayments
            migrationBuilder.AddForeignKey(
                name: "FK_StripePayments_SavedCreditCards_CustomerCreditCardId",
                table: "StripePayments",
                column: "CustomerCreditCardId",
                principalTable: "SavedCreditCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop foreign key from StripePayments
            migrationBuilder.DropForeignKey(
                name: "FK_StripePayments_SavedCreditCards_CustomerCreditCardId",
                table: "StripePayments");

            // Drop index from StripePayments
            migrationBuilder.DropIndex(
                name: "IX_StripePayments_CustomerCreditCardId",
                table: "StripePayments");

            // Add CustomerCreditCardId back to RenewalRequests
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerCreditCardId",
                table: "RenewalRequests",
                type: "uniqueidentifier",
                nullable: true);

            // Migrate data back from StripePayments to RenewalRequests
            migrationBuilder.Sql(@"
                UPDATE rr
                SET rr.CustomerCreditCardId = sp.CustomerCreditCardId
                FROM RenewalRequests rr
                INNER JOIN StripePayments sp ON rr.Id = sp.RenewalRequestId
            ");

            // Drop CustomerCreditCardId from StripePayments
            migrationBuilder.DropColumn(
                name: "CustomerCreditCardId",
                table: "StripePayments");

            // Create index on RenewalRequests.CustomerCreditCardId
            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_CustomerCreditCardId",
                table: "RenewalRequests",
                column: "CustomerCreditCardId");

            // Add foreign key back to RenewalRequests
            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_CustomerCreditCardId",
                table: "RenewalRequests",
                column: "CustomerCreditCardId",
                principalTable: "SavedCreditCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
