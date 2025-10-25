using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHoldersAndSimplifySearchResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop Holders table
            migrationBuilder.DropTable(
                name: "Holders");

            // Drop columns from SearchResults
            migrationBuilder.DropColumn(
                name: "Status",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "RenewalDate",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "CancelledDate",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "CancellationUnderReview",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "AddressForServiceDocuments",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "PrincipalPlaceOfBusiness",
                table: "SearchResults");

            // Add AccountNumber column
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "SearchResults",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Alter RegistrationDate to make it non-nullable
            migrationBuilder.AlterColumn<string>(
                name: "RegistrationDate",
                table: "SearchResults",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate Holders table
            migrationBuilder.CreateTable(
                name: "Holders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SearchResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Abn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holders_SearchResults_SearchResultId",
                        column: x => x.SearchResultId,
                        principalTable: "SearchResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holders_SearchResultId",
                table: "Holders",
                column: "SearchResultId");

            // Drop AccountNumber column
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "SearchResults");

            // Alter RegistrationDate to make it nullable
            migrationBuilder.AlterColumn<string>(
                name: "RegistrationDate",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            // Add back dropped columns
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SearchResults",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenewalDate",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledDate",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationUnderReview",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressForServiceDocuments",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrincipalPlaceOfBusiness",
                table: "SearchResults",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
