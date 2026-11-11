using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class OrderOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AsicCheckedAt",
                table: "RenewalOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "RenewalOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvidenceAt",
                table: "RenewalOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceSource",
                table: "RenewalOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "RenewalOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutcomeAt",
                table: "RenewalOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeBy",
                table: "RenewalOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeReference",
                table: "RenewalOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
            // Orders already closed as renewed elsewhere (marked handled on the sale or attempt,
            // e.g. the 12 Nov 2026 cleanup) keep that as a stored outcome, with its note as evidence.
            migrationBuilder.Sql("""
                UPDATE RenewalOrders
                SET Outcome = 'RenewedElsewhere', OutcomeAt = StageChangedAt, OutcomeBy = 'Marked handled', Evidence = Note
                WHERE Stage = 'Closed' AND Reason = 'RenewedElsewhere' AND Outcome IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AsicCheckedAt",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "EvidenceAt",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "EvidenceSource",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "OutcomeAt",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "OutcomeBy",
                table: "RenewalOrders");

            migrationBuilder.DropColumn(
                name: "OutcomeReference",
                table: "RenewalOrders");
        }
    }
}
