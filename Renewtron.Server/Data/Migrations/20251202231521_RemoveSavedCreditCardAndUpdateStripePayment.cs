using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSavedCreditCardAndUpdateStripePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StripePayments_SavedCreditCards_CustomerCreditCardId",
                table: "StripePayments");

            migrationBuilder.DropTable(
                name: "SavedCreditCards");

            migrationBuilder.DropIndex(
                name: "IX_StripePayments_CustomerCreditCardId",
                table: "StripePayments");

            migrationBuilder.DropColumn(
                name: "CustomerCreditCardId",
                table: "StripePayments");

            migrationBuilder.AddColumn<string>(
                name: "CardBrand",
                table: "StripePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardExpMonth",
                table: "StripePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardExpYear",
                table: "StripePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardLast4",
                table: "StripePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardholderName",
                table: "StripePayments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardBrand",
                table: "StripePayments");

            migrationBuilder.DropColumn(
                name: "CardExpMonth",
                table: "StripePayments");

            migrationBuilder.DropColumn(
                name: "CardExpYear",
                table: "StripePayments");

            migrationBuilder.DropColumn(
                name: "CardLast4",
                table: "StripePayments");

            migrationBuilder.DropColumn(
                name: "CardholderName",
                table: "StripePayments");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerCreditCardId",
                table: "StripePayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "SavedCreditCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardBrand = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CardNumberLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CardholderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EncryptedCardNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedCvc = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiryMonth = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiryYear = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCreditCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripePayments_CustomerCreditCardId",
                table: "StripePayments",
                column: "CustomerCreditCardId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCreditCards_CreatedAt",
                table: "SavedCreditCards",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCreditCards_IpAddress",
                table: "SavedCreditCards",
                column: "IpAddress");

            migrationBuilder.AddForeignKey(
                name: "FK_StripePayments_SavedCreditCards_CustomerCreditCardId",
                table: "StripePayments",
                column: "CustomerCreditCardId",
                principalTable: "SavedCreditCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
