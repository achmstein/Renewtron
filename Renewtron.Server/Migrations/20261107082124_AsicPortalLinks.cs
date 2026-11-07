using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AsicPortalLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PortalLinkedAt",
                table: "ClientBusinessNames",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AsicPortalLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientBusinessNameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    AsicKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DateOfBirth = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DateOfBirthSource = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OntraportContactId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DeclareAs = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LinkOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmailOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmailReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RepsOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RepsRemoved = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RepsReferences = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HolderOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HolderType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HolderReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailedAtStep = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Screenshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OntraportResult = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsicPortalLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AsicPortalLinks_ClientBusinessNames_ClientBusinessNameId",
                        column: x => x.ClientBusinessNameId,
                        principalTable: "ClientBusinessNames",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AsicPortalLinks_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsicPortalLinks_ClientBusinessNameId",
                table: "AsicPortalLinks",
                column: "ClientBusinessNameId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicPortalLinks_ClientId",
                table: "AsicPortalLinks",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AsicPortalLinks_Status",
                table: "AsicPortalLinks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsicPortalLinks");

            migrationBuilder.DropColumn(
                name: "PortalLinkedAt",
                table: "ClientBusinessNames");
        }
    }
}
