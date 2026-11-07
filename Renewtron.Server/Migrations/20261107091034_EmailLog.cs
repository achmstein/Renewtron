using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class EmailLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "OutboundMessages",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "OutboundMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "OutboundMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BodyText",
                table: "OutboundMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryAt",
                table: "OutboundMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryDetail",
                table: "OutboundMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "OutboundMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "OutboundMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "OutboundMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "OutboundMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalRequestId",
                table: "OutboundMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResendOfId",
                table: "OutboundMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_ProviderMessageId",
                table: "OutboundMessages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_Recipient",
                table: "OutboundMessages",
                column: "Recipient");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_RenewalRequestId",
                table: "OutboundMessages",
                column: "RenewalRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_ProviderMessageId",
                table: "OutboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_Recipient",
                table: "OutboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessages_RenewalRequestId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "BodyText",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryAt",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryDetail",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "RenewalRequestId",
                table: "OutboundMessages");

            migrationBuilder.DropColumn(
                name: "ResendOfId",
                table: "OutboundMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "OutboundMessages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
