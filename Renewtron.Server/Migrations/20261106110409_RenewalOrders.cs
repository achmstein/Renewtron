using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class RenewalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RenewalOrderId",
                table: "RenewalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RenewalOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientBusinessNameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Years = table.Column<int>(type: "int", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NextActionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StageChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CurrentAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    RenewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenewalOrders_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_RenewalOrderId",
                table: "RenewalRequests",
                column: "RenewalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalOrders_Abn_NameKey",
                table: "RenewalOrders",
                columns: new[] { "Abn", "NameKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalOrders_ClientId",
                table: "RenewalOrders",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalOrders_Source_SourceId",
                table: "RenewalOrders",
                columns: new[] { "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalOrders_Stage",
                table: "RenewalOrders",
                column: "Stage");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_RenewalOrders_RenewalOrderId",
                table: "RenewalRequests",
                column: "RenewalOrderId",
                principalTable: "RenewalOrders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_RenewalOrders_RenewalOrderId",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "RenewalOrders");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_RenewalOrderId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "RenewalOrderId",
                table: "RenewalRequests");
        }
    }
}
