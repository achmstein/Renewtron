using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkRenewalUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkRenewalUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RenewalDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalYears = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    RenewalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkRenewalUploads", x => x.Id);
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkRenewalUploads");
        }
    }
}
