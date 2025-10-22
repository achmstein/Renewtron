using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneToOneRenewalRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_SearchResultId",
                table: "RenewalRequests");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_SearchResultId",
                table: "RenewalRequests",
                column: "SearchResultId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenewalRequests_SearchResultId",
                table: "RenewalRequests");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalRequests_SearchResultId",
                table: "RenewalRequests",
                column: "SearchResultId");
        }
    }
}
