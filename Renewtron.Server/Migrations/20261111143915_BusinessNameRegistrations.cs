using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class BusinessNameRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessNameRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Years = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ServiceAddress_Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ServiceAddress_StreetNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ServiceAddress_StreetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceAddress_StreetType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ServiceAddress_Suburb = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceAddress_State = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ServiceAddress_Postcode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    PrincipalAddress_Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrincipalAddress_StreetNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrincipalAddress_StreetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrincipalAddress_StreetType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrincipalAddress_Suburb = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrincipalAddress_State = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PrincipalAddress_Postcode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    NameTakenLocally = table.Column<bool>(type: "bit", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AsicTransactionReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AbrEntityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AbrEntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    NameVerdict = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NameVerdictText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AsicPayBy = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AsicFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LodgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BpayBillerCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BpayReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AirwallexTransferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AirwallexTransferStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BpayPaymentAttempts = table.Column<int>(type: "int", nullable: false),
                    BpayPaymentError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AsicPaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AsicTransactionStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AsicPaymentStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AsicCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtStep = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PageText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    VisitorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessNameRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredBusinessNames_Name_ImportId",
                table: "RegisteredBusinessNames",
                columns: new[] { "Name", "ImportId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessNameRegistrations_CreatedAt",
                table: "BusinessNameRegistrations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessNameRegistrations_Status",
                table: "BusinessNameRegistrations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessNameRegistrations_StripePaymentIntentId",
                table: "BusinessNameRegistrations",
                column: "StripePaymentIntentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessNameRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_RegisteredBusinessNames_Name_ImportId",
                table: "RegisteredBusinessNames");
        }
    }
}
