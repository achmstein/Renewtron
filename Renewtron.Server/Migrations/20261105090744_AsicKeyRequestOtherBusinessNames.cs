using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renewtron.Migrations
{
    /// <inheritdoc />
    public partial class AsicKeyRequestOtherBusinessNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Question",
                table: "AsicKeyRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherBusinessNames",
                table: "AsicKeyRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            // Requests not sent yet (Pending 0, Failed 3, Manual 4) pick up the client's other
            // names from the active register import. Their enquiry text is rendered live until
            // sent, so it follows automatically; sent rows keep what actually went to ASIC.
            // FOR XML PATH rather than STRING_AGG so it also runs on SQL Server 2016 (LocalDB).
            migrationBuilder.Sql("""
                UPDATE r SET OtherBusinessNames = o.Names
                FROM AsicKeyRequests r
                CROSS APPLY (
                    SELECT NULLIF(STUFF((
                        SELECT CHAR(10) + x.Name
                        FROM (
                            SELECT DISTINCT n.Name
                            FROM RegisteredBusinessNames n
                            JOIN BusinessNameImports i ON i.Id = n.ImportId AND i.IsActive = 1
                            WHERE n.Abn = CAST(r.Abn AS varchar(11)) AND n.Name <> r.BusinessName
                        ) x
                        ORDER BY x.Name
                        FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, ''), '') AS Names
                ) o
                WHERE r.Status IN (0, 3, 4) AND LEN(r.Abn) = 11 AND o.Names IS NOT NULL AND LEN(o.Names) <= 4000;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OtherBusinessNames",
                table: "AsicKeyRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Question",
                table: "AsicKeyRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
