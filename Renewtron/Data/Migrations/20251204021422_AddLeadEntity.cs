using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeadId",
                table: "RenewalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    OutcomeMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReminderOptIn = table.Column<bool>(type: "bit", nullable: false),
                    ConvertedToRenewal = table.Column<bool>(type: "bit", nullable: false),
                    ConvertedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SearchLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_SearchLogs_SearchLogId",
                        column: x => x.SearchLogId,
                        principalTable: "SearchLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_LeadId",
                table: "RenewalRequests",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Abn",
                table: "Leads",
                column: "Abn");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CreatedAt",
                table: "Leads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Email",
                table: "Leads",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Outcome",
                table: "Leads",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_SearchLogId",
                table: "Leads",
                column: "SearchLogId");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_Leads_LeadId",
                table: "RenewalRequests",
                column: "LeadId",
                principalTable: "Leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_Leads_LeadId",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_LeadId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "LeadId",
                table: "RenewalRequests");
        }
    }
}
