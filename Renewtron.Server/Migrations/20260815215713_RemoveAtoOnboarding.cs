using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAtoOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtoOnboardingCompletedAt",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AtoOnboardingJobId",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AtoOnboardingResultJson",
                table: "RenewalRequests");

            migrationBuilder.DropColumn(
                name: "AtoOnboardingStatus",
                table: "RenewalRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AtoOnboardingCompletedAt",
                table: "RenewalRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingJobId",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingResultJson",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtoOnboardingStatus",
                table: "RenewalRequests",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
