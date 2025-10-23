using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRenewalRequestCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the deprecated Completed column
            migrationBuilder.DropColumn(
                name: "Completed",
                table: "RenewalRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the Completed column
            migrationBuilder.AddColumn<bool>(
                name: "Completed",
                table: "RenewalRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Restore data based on Status field
            migrationBuilder.Sql(@"
                UPDATE RenewalRequests
                SET Completed = CASE
                    WHEN Status = 2 THEN 1  -- Completed status
                    ELSE 0
                END
            ");
        }
    }
}
