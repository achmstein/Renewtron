using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AddAsicKeyNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AsicKeyNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    From = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DownloadUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AsicKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OntraportContactIds = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OntraportContactsUpdated = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PdfTextExcerpt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsicKeyNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_CreatedAt",
                table: "AsicKeyNotifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_MessageId",
                table: "AsicKeyNotifications",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_ReceivedAt",
                table: "AsicKeyNotifications",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_Status",
                table: "AsicKeyNotifications",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsicKeyNotifications");
        }
    }
}
