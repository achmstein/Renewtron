using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class KeepAsicLetterPdfs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentKind",
                table: "AsicKeyNotifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PdfCheckedAt",
                table: "AsicKeyNotifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PdfSavedAt",
                table: "AsicKeyNotifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AsicKeyNotificationPdfs",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsicKeyNotificationPdfs", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_AsicKeyNotificationPdfs_AsicKeyNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "AsicKeyNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsicKeyNotifications_AsicKey",
                table: "AsicKeyNotifications",
                column: "AsicKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsicKeyNotificationPdfs");

            migrationBuilder.DropIndex(
                name: "IX_AsicKeyNotifications_AsicKey",
                table: "AsicKeyNotifications");

            migrationBuilder.DropColumn(
                name: "DocumentKind",
                table: "AsicKeyNotifications");

            migrationBuilder.DropColumn(
                name: "PdfCheckedAt",
                table: "AsicKeyNotifications");

            migrationBuilder.DropColumn(
                name: "PdfSavedAt",
                table: "AsicKeyNotifications");
        }
    }
}
