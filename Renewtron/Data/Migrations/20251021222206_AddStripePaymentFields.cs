using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_SavedCreditCardId",
                table: "RenewalRequests");

            migrationBuilder.RenameColumn(
                name: "SavedCreditCardId",
                table: "RenewalRequests",
                newName: "CustomerCreditCardId");

            migrationBuilder.RenameIndex(
                name: "IX_RenewalRequests_SavedCreditCardId",
                table: "RenewalRequests",
                newName: "IX_RenewalRequests_CustomerCreditCardId");

            migrationBuilder.AddColumn<decimal>(
                name: "AsicAmount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomerAmount",
                table: "RenewalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "StripePaidAt",
                table: "RenewalRequests",
                type: "datetime2",
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

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_CustomerCreditCardId",
                table: "RenewalRequests",
                column: "CustomerCreditCardId",
                principalTable: "SavedCreditCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_CustomerCreditCardId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AsicAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "CustomerAmount",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaidAt",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "StripePaymentStatus",
                table: "RenewalRequests");

            migrationBuilder.RenameColumn(
                name: "CustomerCreditCardId",
                table: "RenewalRequests",
                newName: "SavedCreditCardId");

            migrationBuilder.RenameIndex(
                name: "IX_RenewalRequests_CustomerCreditCardId",
                table: "RenewalRequests",
                newName: "IX_RenewalRequests_SavedCreditCardId");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_SavedCreditCards_SavedCreditCardId",
                table: "RenewalRequests",
                column: "SavedCreditCardId",
                principalTable: "SavedCreditCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
