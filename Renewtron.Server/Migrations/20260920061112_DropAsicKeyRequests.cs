using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class DropAsicKeyRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsicKeyRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AsicKeyRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AsicKeyNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OntraportSaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AsicReferenceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CanAutoRetry = table.Column<bool>(type: "bit", nullable: false),
                    CaptchaSolves = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FamilyName = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GivenNames = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    KeyReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OntraportContactId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsicKeyRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AsicKeyRequests_AsicKeyNotifications_AsicKeyNotificationId",
                        column: x => x.AsicKeyNotificationId,
                        principalTable: "AsicKeyNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AsicKeyRequests_OntraportSales_OntraportSaleId",
                        column: x => x.OntraportSaleId,
                        principalTable: "OntraportSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_Abn",
                table: "AsicKeyRequests",
                column: "Abn");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_AsicKeyNotificationId",
                table: "AsicKeyRequests",
                column: "AsicKeyNotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_BusinessName",
                table: "AsicKeyRequests",
                column: "BusinessName");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_CreatedAt",
                table: "AsicKeyRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_OntraportSaleId",
                table: "AsicKeyRequests",
                column: "OntraportSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_Status",
                table: "AsicKeyRequests",
                column: "Status");
        }
    }
}
