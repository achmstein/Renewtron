using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class LeadProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NamesShownAt",
                table: "Leads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentFollowUpSentAt",
                table: "Leads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Progress",
                table: "Leads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReachedPaymentAt",
                table: "Leads",
                type: "datetime2",
                nullable: true);
            // How far each past visit got, from what is already stored: paid, a payment_viewed
            // funnel event, or names to pick from. What ASIC said goes back where paying had
            // overwritten it (they paid, so ASIC had offered the names).
            migrationBuilder.Sql("""
                UPDATE Leads SET Progress = 1, NamesShownAt = CreatedAt
                WHERE Outcome IN (1, 5) OR ConvertedToRenewal = 1;

                UPDATE l SET Progress = 2, ReachedPaymentAt = f.FirstAt, NamesShownAt = ISNULL(l.NamesShownAt, l.CreatedAt)
                FROM Leads l
                JOIN (SELECT LeadId, MIN(CreatedAt) AS FirstAt FROM FunnelEvents WHERE Step = 'payment_viewed' AND LeadId IS NOT NULL GROUP BY LeadId) f
                  ON f.LeadId = l.Id;

                UPDATE Leads SET Progress = 3 WHERE ConvertedToRenewal = 1;
                UPDATE Leads SET Outcome = 1 WHERE Outcome = 5;

                -- Past drop-offs are not emailed now; only visits from here on get the follow-up.
                UPDATE Leads SET PaymentFollowUpSentAt = ReachedPaymentAt WHERE Progress = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NamesShownAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PaymentFollowUpSentAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ReachedPaymentAt",
                table: "Leads");
        }
    }
}
