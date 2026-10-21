using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeCashTurnoverSystemReference20261018 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(name: "OfficeCashTurnoverReferenceNumberSeq");

            // Existing turnovers are kept as they are: they get the next numbers in recording order and a year taken from their own turnover date.
            migrationBuilder.AddColumn<int>(name: "ReferenceYear", table: "OfficeCashTurnovers", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.Sql("UPDATE \"OfficeCashTurnovers\" SET \"ReferenceYear\" = EXTRACT(YEAR FROM \"TurnoverDate\")::integer;");
            migrationBuilder.AlterColumn<int>(name: "ReferenceYear", table: "OfficeCashTurnovers", type: "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldDefaultValue: 0);

            migrationBuilder.AddColumn<long>(name: "ReferenceNumber", table: "OfficeCashTurnovers", type: "bigint", nullable: false,
                defaultValueSql: "nextval('\"OfficeCashTurnoverReferenceNumberSeq\"')");

            migrationBuilder.AddColumn<string>(name: "ReferenceCode", table: "OfficeCashTurnovers", type: "character varying(40)", maxLength: 40, nullable: false,
                computedColumnSql: "'OCT-' || \"ReferenceYear\"::text || '-' || repeat('0', greatest(6 - length(\"ReferenceNumber\"::text), 0)) || \"ReferenceNumber\"::text", stored: true);

            migrationBuilder.AlterColumn<string>(name: "Reference", table: "OfficeCashTurnovers", type: "character varying(100)", maxLength: 100, nullable: true,
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);

            migrationBuilder.DropCheckConstraint(name: "CK_OfficeCashTurnovers_Evidence", table: "OfficeCashTurnovers");
            migrationBuilder.AddCheckConstraint(name: "CK_OfficeCashTurnovers_Evidence", table: "OfficeCashTurnovers",
                sql: "length(btrim(\"TurnedOverBy\")) > 0 AND (\"Reference\" IS NULL OR length(btrim(\"Reference\")) > 0)");

            migrationBuilder.CreateIndex(name: "IX_OfficeCashTurnovers_MunicipalityId_ReferenceYear_ReferenceN~", table: "OfficeCashTurnovers",
                columns: new[] { "MunicipalityId", "ReferenceYear", "ReferenceNumber" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_OfficeCashTurnovers_MunicipalityId_ReferenceYear_ReferenceN~", table: "OfficeCashTurnovers");
            migrationBuilder.DropCheckConstraint(name: "CK_OfficeCashTurnovers_Evidence", table: "OfficeCashTurnovers");
            migrationBuilder.Sql("UPDATE \"OfficeCashTurnovers\" SET \"Reference\" = \"ReferenceCode\" WHERE \"Reference\" IS NULL;");
            migrationBuilder.AddCheckConstraint(name: "CK_OfficeCashTurnovers_Evidence", table: "OfficeCashTurnovers",
                sql: "length(btrim(\"Reference\")) > 0 AND length(btrim(\"TurnedOverBy\")) > 0");
            migrationBuilder.AlterColumn<string>(name: "Reference", table: "OfficeCashTurnovers", type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "",
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);
            migrationBuilder.DropColumn(name: "ReferenceCode", table: "OfficeCashTurnovers");
            migrationBuilder.DropColumn(name: "ReferenceNumber", table: "OfficeCashTurnovers");
            migrationBuilder.DropColumn(name: "ReferenceYear", table: "OfficeCashTurnovers");
            migrationBuilder.DropSequence(name: "OfficeCashTurnoverReferenceNumberSeq");
        }
    }
}
