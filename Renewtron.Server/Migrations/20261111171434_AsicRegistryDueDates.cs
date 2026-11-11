using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AsicRegistryDueDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDateCheckedAt",
                table: "ClientBusinessNames",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDateCheckedAt",
                table: "ClientBusinessNames");
        }
    }
}
