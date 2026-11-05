using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AddClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientBusinessNameId",
                table: "RenewalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "RenewalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "OutboundMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientBusinessNameId",
                table: "OntraportSales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "OntraportSales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientBusinessNameId",
                table: "BulkRenewalUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "BulkRenewalUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "AsicKeyRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientBusinessNameId",
                table: "AsicKeyNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Suburb = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Postcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TfnEncrypted = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HasPurchased = table.Column<bool>(type: "bit", nullable: false),
                    ReminderOptIn = table.Column<bool>(type: "bit", nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    PossibleDuplicateOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_Clients_PossibleDuplicateOfId",
                        column: x => x.PossibleDuplicateOfId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientAbns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    RegisterCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAbns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAbns_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientBusinessNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegistrationDate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    AsicAccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RenewalDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDateSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AsicKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AsicKeyReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AsicKeyNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastRenewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRenewalYears = table.Column<int>(type: "int", nullable: true),
                    LastRenewalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientBusinessNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientBusinessNames_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OntraportContactId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEmails_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_ClientBusinessNameId",
                table: "RenewalRequests",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_ClientId",
                table: "RenewalRequests",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_ClientId",
                table: "OutboundMessages",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_ClientBusinessNameId",
                table: "OntraportSales",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_OntraportSales_ClientId",
                table: "OntraportSales",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ClientId",
                table: "Leads",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_ClientBusinessNameId",
                table: "BulkRenewalUploads",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkRenewalUploads_ClientId",
                table: "BulkRenewalUploads",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyRequests_ClientId",
                table: "AsicKeyRequests",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_ClientBusinessNameId",
                table: "AsicKeyNotifications",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAbns_Abn",
                table: "ClientAbns",
                column: "Abn");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAbns_ClientId_Abn",
                table: "ClientAbns",
                columns: new[] { "ClientId", "Abn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientBusinessNames_Abn_Name",
                table: "ClientBusinessNames",
                columns: new[] { "Abn", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientBusinessNames_ClientId",
                table: "ClientBusinessNames",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientBusinessNames_RenewalDueDate",
                table: "ClientBusinessNames",
                column: "RenewalDueDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmails_ClientId",
                table: "ClientEmails",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmails_Email",
                table: "ClientEmails",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmails_OntraportContactId",
                table: "ClientEmails",
                column: "OntraportContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_FullName",
                table: "Clients",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LastSeenAt",
                table: "Clients",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_PossibleDuplicateOfId",
                table: "Clients",
                column: "PossibleDuplicateOfId");

            migrationBuilder.AddForeignKey(
                name: "FK_AsicKeyNotifications_ClientBusinessNames_ClientBusinessNameId",
                table: "AsicKeyNotifications",
                column: "ClientBusinessNameId",
                principalTable: "ClientBusinessNames",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AsicKeyRequests_Clients_ClientId",
                table: "AsicKeyRequests",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BulkRenewalUploads_ClientBusinessNames_ClientBusinessNameId",
                table: "BulkRenewalUploads",
                column: "ClientBusinessNameId",
                principalTable: "ClientBusinessNames",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BulkRenewalUploads_Clients_ClientId",
                table: "BulkRenewalUploads",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Clients_ClientId",
                table: "Leads",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OntraportSales_ClientBusinessNames_ClientBusinessNameId",
                table: "OntraportSales",
                column: "ClientBusinessNameId",
                principalTable: "ClientBusinessNames",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OntraportSales_Clients_ClientId",
                table: "OntraportSales",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundMessages_Clients_ClientId",
                table: "OutboundMessages",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_ClientBusinessNames_ClientBusinessNameId",
                table: "RenewalRequests",
                column: "ClientBusinessNameId",
                principalTable: "ClientBusinessNames",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalRequests_Clients_ClientId",
                table: "RenewalRequests",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AsicKeyNotifications_ClientBusinessNames_ClientBusinessNameId",
                table: "AsicKeyNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AsicKeyRequests_Clients_ClientId",
                table: "AsicKeyRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_BulkRenewalUploads_ClientBusinessNames_ClientBusinessNameId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropForeignKey(
                name: "FK_BulkRenewalUploads_Clients_ClientId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Clients_ClientId",
                table: "Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_OntraportSales_ClientBusinessNames_ClientBusinessNameId",
                table: "OntraportSales");

            migrationBuilder.DropForeignKey(
                name: "FK_OntraportSales_Clients_ClientId",
                table: "OntraportSales");

            migrationBuilder.DropForeignKey(
                name: "FK_OutboundMessages_Clients_ClientId",
                table: "OutboundMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_ClientBusinessNames_ClientBusinessNameId",
                table: "RenewalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_RenewalRequests_Clients_ClientId",
                table: "RenewalRequests");

            migrationBuilder.DropTable(
                name: "ClientAbns");

            migrationBuilder.DropTable(
                name: "ClientBusinessNames");

            migrationBuilder.DropTable(
                name: "ClientEmails");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_ClientBusinessNameId",
                table: "RenewalRequests");

            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_ClientId",
                table: "RenewalRequests");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_ClientId",
                table: "OutboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_OntraportSales_ClientBusinessNameId",
                table: "OntraportSales");

            migrationBuilder.DropIndex(
                name: "IX_OntraportSales_ClientId",
                table: "OntraportSales");

            migrationBuilder.DropIndex(
                name: "IX_Leads_ClientId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_BulkRenewalUploads_ClientBusinessNameId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropIndex(
                name: "IX_BulkRenewalUploads_ClientId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropIndex(
                name: "IX_AsicKeyRequests_ClientId",
                table: "AsicKeyRequests");

            migrationBuilder.DropIndex(
                name: "IX_AsicKeyNotifications_ClientBusinessNameId",
                table: "AsicKeyNotifications");

            migrationBuilder.DropColumn(
                name: "ClientBusinessNameId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "ClientBusinessNameId",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ClientBusinessNameId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "BulkRenewalUploads");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "AsicKeyRequests");

            migrationBuilder.DropColumn(
                name: "ClientBusinessNameId",
                table: "AsicKeyNotifications");
        }
    }
}
