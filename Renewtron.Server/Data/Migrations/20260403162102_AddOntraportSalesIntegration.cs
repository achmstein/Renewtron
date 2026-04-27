using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOntraportSalesIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "RenewalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OntraportSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OntraportContactId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OntraportTransactionId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DateOfBirth = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BusinessNameOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RenewalDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalYears = table.Column<int>(type: "int", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RenewalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OntraportSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OntraportSales_RenewalRequests_RenewalRequestId",
                        column: x => x.RenewalRequestId,
                        principalTable: "RenewalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_Abn",
                table: "OntraportSales",
                column: "Abn");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_OntraportContactId",
                table: "OntraportSales",
                column: "OntraportContactId");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_RenewalDueDate",
                table: "OntraportSales",
                column: "RenewalDueDate");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_RenewalRequestId",
                table: "OntraportSales",
                column: "RenewalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_Status",
                table: "OntraportSales",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "RenewalRequests");
        }
    }
}
