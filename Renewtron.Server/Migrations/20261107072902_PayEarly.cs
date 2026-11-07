using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class PayEarly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PayEarly",
                table: "SearchResults",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayEarly",
                table: "SearchResults");
        }
    }
}
