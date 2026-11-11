using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class SearchRetryAndDropBulkUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bulk uploads are gone: their paid-renewal records go with them. The ASIC runs they
            // started stay as history, no longer attached to an order.
            migrationBuilder.Sql("""
                UPDATE RenewalRequests SET RenewalOrderId = NULL
                WHERE RenewalOrderId IN (SELECT Id FROM RenewalOrders WHERE Source = 'Bulk');
                DELETE FROM RenewalOrders WHERE Source = 'Bulk';
                """);

            migrationBuilder.DropTable(
                name: "BulkRenewalUploads");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSearchAt",
                table: "OntraportSales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchAttempts",
                table: "OntraportSales",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextSearchAt",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "SearchAttempts",
                table: "OntraportSales");

            migrationBuilder.CreateTable(
                name: "BulkRenewalUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RenewalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientBusinessNameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalYears = table.Column<int>(type: "int", nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkRenewalUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulkRenewalUploads_ClientBusinessNames_ClientBusinessNameId",
                        column: x => x.ClientBusinessNameId,
                        principalTable: "ClientBusinessNames",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BulkRenewalUploads_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BulkRenewalUploads_RenewalRequests_RenewalRequestId",
                        column: x => x.RenewalRequestId,
                        principalTable: "RenewalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_Abn",
                table: "BulkRenewalUploads",
                column: "Abn");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_ClientBusinessNameId",
                table: "BulkRenewalUploads",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_ClientId",
                table: "BulkRenewalUploads",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_RenewalDueDate",
                table: "BulkRenewalUploads",
                column: "RenewalDueDate");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_RenewalRequestId",
                table: "BulkRenewalUploads",
                column: "RenewalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_Status",
                table: "BulkRenewalUploads",
                column: "Status");
        }
    }
}
