using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class BusinessNamesUnderAbn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Holders first, filled from whatever is there (production's client tables are
            // still empty, but a dev database may not be), so the new foreign keys hold.
            migrationBuilder.CreateTable(
                name: "AbnHolders",
                columns: table => new
                {
                    Abn = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    RegisterCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbnHolders", x => x.Abn);
                });

            migrationBuilder.Sql("""
                INSERT INTO AbnHolders (Abn, RegisterCheckedAt, CreatedAt)
                SELECT Abn, MIN(RegisterCheckedAt), MIN(CreatedAt)
                FROM (
                    SELECT Abn, RegisterCheckedAt, CreatedAt FROM ClientAbns
                    UNION ALL
                    SELECT Abn, NULL, CreatedAt FROM ClientBusinessNames
                ) x
                GROUP BY Abn;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsReminderContact",
                table: "ClientAbns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The earliest person on each ABN gets its reminders.
            migrationBuilder.Sql("""
                UPDATE a SET IsReminderContact = 1
                FROM ClientAbns a
                JOIN (SELECT Id, ROW_NUMBER() OVER (PARTITION BY Abn ORDER BY CreatedAt, Id) AS rn FROM ClientAbns) r ON r.Id = a.Id
                WHERE r.rn = 1;
                """);

            // Duplicate flags were set on a shared ABN alone; now a second person on an ABN is
            // just another contact, and only the same name or mobile (last nine digits) as the
            // flagged-against client keeps the flag.
            migrationBuilder.Sql("""
                UPDATE c SET PossibleDuplicateOfId = NULL
                FROM Clients c
                JOIN Clients o ON o.Id = c.PossibleDuplicateOfId
                CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(c.MobileNumber, ''), ' ', ''), '+', ''), '-', ''), '(', ''), ')', '') AS d) m1
                CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(o.MobileNumber, ''), ' ', ''), '+', ''), '-', ''), '(', ''), ')', '') AS d) m2
                WHERE NOT (
                    (LTRIM(RTRIM(c.FullName)) <> '' AND LOWER(LTRIM(RTRIM(c.FullName))) = LOWER(LTRIM(RTRIM(o.FullName))))
                    OR (LEN(m1.d) >= 9 AND RIGHT(m1.d, 9) = RIGHT(m2.d, 9))
                );
                """);

            migrationBuilder.DropColumn(
                name: "RegisterCheckedAt",
                table: "ClientAbns");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientBusinessNames_Clients_ClientId",
                table: "ClientBusinessNames");

            migrationBuilder.DropIndex(
                name: "IX_ClientBusinessNames_ClientId",
                table: "ClientBusinessNames");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "ClientBusinessNames");

            // dd/MM/yyyy text to a date (style 103); anything unreadable becomes NULL and the
            // next register read fills it in.
            migrationBuilder.AddColumn<DateOnly>(
                name: "RegistrationDateValue",
                table: "ClientBusinessNames",
                type: "date",
                nullable: true);
            migrationBuilder.Sql("UPDATE ClientBusinessNames SET RegistrationDateValue = TRY_CONVERT(date, RegistrationDate, 103);");
            migrationBuilder.DropColumn(
                name: "RegistrationDate",
                table: "ClientBusinessNames");
            migrationBuilder.RenameColumn(
                name: "RegistrationDateValue",
                table: "ClientBusinessNames",
                newName: "RegistrationDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAbns_Abn_ReminderContact",
                table: "ClientAbns",
                column: "Abn",
                unique: true,
                filter: "[IsReminderContact] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AbnHolders_RegisterCheckedAt",
                table: "AbnHolders",
                column: "RegisterCheckedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientAbns_AbnHolders_Abn",
                table: "ClientAbns",
                column: "Abn",
                principalTable: "AbnHolders",
                principalColumn: "Abn");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBusinessNames_AbnHolders_Abn",
                table: "ClientBusinessNames",
                column: "Abn",
                principalTable: "AbnHolders",
                principalColumn: "Abn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientAbns_AbnHolders_Abn",
                table: "ClientAbns");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientBusinessNames_AbnHolders_Abn",
                table: "ClientBusinessNames");

            migrationBuilder.DropTable(
                name: "AbnHolders");

            migrationBuilder.DropIndex(
                name: "IX_ClientAbns_Abn_ReminderContact",
                table: "ClientAbns");

            migrationBuilder.DropColumn(
                name: "IsReminderContact",
                table: "ClientAbns");

            migrationBuilder.AlterColumn<string>(
                name: "RegistrationDate",
                table: "ClientBusinessNames",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "ClientBusinessNames",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisterCheckedAt",
                table: "ClientAbns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientBusinessNames_ClientId",
                table: "ClientBusinessNames",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBusinessNames_Clients_ClientId",
                table: "ClientBusinessNames",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
