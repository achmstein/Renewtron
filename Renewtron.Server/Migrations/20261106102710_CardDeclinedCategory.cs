using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class CardDeclinedCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Declines at ASIC's last payment step were filed as "payment risk", which blocks every
            // retry. ASIC took nothing on a decline, so they are card declines and can be retried.
            migrationBuilder.Sql("""
                UPDATE RenewalRequests SET ErrorCategory = 'CardDeclined'
                WHERE Status = 3 AND ErrorCategory = 'PaymentRisk'
                  AND FailedAtStep = 'Complete Payment Action' AND ErrorMessage LIKE '%declined%'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE RenewalRequests SET ErrorCategory = 'PaymentRisk' WHERE ErrorCategory = 'CardDeclined' AND FailedAtStep = 'Complete Payment Action'");
        }
    }
}
