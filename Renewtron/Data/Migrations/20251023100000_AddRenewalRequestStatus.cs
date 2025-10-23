using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRenewalRequestStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Status column with default value of Pending (0)
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "RenewalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Update existing records: set Status based on Completed field
            // If Completed = true and ErrorMessage is null -> Completed (2)
            // If Completed = true and ErrorMessage is not null -> Failed (3)
            // If Completed = false -> Pending (0)
            migrationBuilder.Sql(@"
                UPDATE RenewalRequests
                SET Status = CASE
                    WHEN Completed = 1 AND ErrorMessage IS NULL THEN 2  -- Completed
                    WHEN Completed = 1 AND ErrorMessage IS NOT NULL THEN 3  -- Failed
                    WHEN ErrorMessage IS NOT NULL THEN 3  -- Failed (even if not marked as completed)
                    ELSE 0  -- Pending (default)
                END
            ");

            // Create index on Status for better query performance
            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_Status",
                table: "RenewalRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_Status",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RenewalRequests");
        }
    }
}
