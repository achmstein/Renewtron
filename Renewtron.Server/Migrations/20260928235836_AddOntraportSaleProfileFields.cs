using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AddOntraportSaleProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "OntraportSales",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "OntraportSales",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "OntraportSales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suburb",
                table: "OntraportSales",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TfnEncrypted",
                table: "OntraportSales",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "State",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "Suburb",
                table: "OntraportSales");

            migrationBuilder.DropColumn(
                name: "TfnEncrypted",
                table: "OntraportSales");
        }
    }
}
