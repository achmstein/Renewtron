using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTfnAndAtoOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tfn",
                table: "Leads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tfn",
                table: "RenewalRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingJobId",
                table: "RenewalRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingStatus",
                table: "RenewalRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingResultJson",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AtoOnboardingCompletedAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Tfn", table: "Leads");
            migrationBuilder.DropColumn(name: "Tfn", table: "RenewalRequests");
            migrationBuilder.DropColumn(name: "AtoOnboardingJobId", table: "RenewalRequests");
            migrationBuilder.DropColumn(name: "AtoOnboardingStatus", table: "RenewalRequests");
            migrationBuilder.DropColumn(name: "AtoOnboardingResultJson", table: "RenewalRequests");
            migrationBuilder.DropColumn(name: "AtoOnboardingCompletedAt", table: "RenewalRequests");
        }
    }
}
