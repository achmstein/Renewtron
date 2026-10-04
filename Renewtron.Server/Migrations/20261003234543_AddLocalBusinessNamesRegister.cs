using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalBusinessNamesRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "SearchResults",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "SearchLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VerificationError",
                table: "SearchLogs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "SearchLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessNameImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceModified = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessNameImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegisteredBusinessNames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegistrationDate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredBusinessNames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredBusinessNames_Abn_ImportId",
                table: "RegisteredBusinessNames",
                columns: new[] { "Abn", "ImportId" })
                .Annotation("SqlServer:Include", new[] { "Name", "RegistrationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredBusinessNames_ImportId",
                table: "RegisteredBusinessNames",
                column: "ImportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessNameImports");

            migrationBuilder.DropTable(
                name: "RegisteredBusinessNames");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "SearchResults");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SearchLogs");

            migrationBuilder.DropColumn(
                name: "VerificationError",
                table: "SearchLogs");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "SearchLogs");
        }
    }
}
