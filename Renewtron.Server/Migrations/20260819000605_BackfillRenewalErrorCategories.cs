using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class BackfillRenewalErrorCategories : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Mirrors RenewalErrorClassifier for the failed rows created before ErrorCategory
        /// existed, so the category breakdown and success-rate stats cover history too.
        /// Order matters: the specific matches run first, everything left is Transient.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE RenewalRequests SET ErrorCategory = 'PaymentRisk'
                WHERE Status = 3 AND ErrorCategory IS NULL
                  AND (FailedAtStep = 'Complete Payment Action' OR ErrorMessage LIKE '%Payment processed but completion failed%');

                UPDATE RenewalRequests SET ErrorCategory = 'NotDueYet'
                WHERE Status = 3 AND ErrorCategory IS NULL AND ErrorMessage LIKE '%not due for renewal%';

                UPDATE RenewalRequests SET ErrorCategory = 'AlreadyInProgress'
                WHERE Status = 3 AND ErrorCategory IS NULL AND ErrorMessage LIKE '%already in progress%';

                UPDATE RenewalRequests SET ErrorCategory = 'Terminal'
                WHERE Status = 3 AND ErrorCategory IS NULL
                  AND (ErrorMessage LIKE '%not found under ABN%'
                    OR ErrorMessage LIKE '%No business names found%'
                    OR ErrorMessage LIKE '%Could not find business name%'
                    OR ErrorMessage LIKE '%check the ABN%'
                    OR ErrorMessage LIKE '%declined%');

                UPDATE RenewalRequests SET ErrorCategory = 'Transient'
                WHERE Status = 3 AND ErrorCategory IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill only — reverting the schema doesn't require clearing the values,
            // but clear them anyway so Down returns to the pre-migration state.
            migrationBuilder.Sql("UPDATE RenewalRequests SET ErrorCategory = NULL WHERE Status = 3;");
        }
    }
}
