using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeStallNoToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Stalls_FacilityId_Section_StallNo\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Stalls_FacilityId_StallNo\";");

            // int -> varchar needs an explicit USING cast in PostgreSQL.
            migrationBuilder.Sql(
                "ALTER TABLE \"Stalls\" ALTER COLUMN \"StallNo\" TYPE character varying(20) USING \"StallNo\"::character varying(20);");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "Section", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Stalls_FacilityId_Section_StallNo\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Stalls_FacilityId_StallNo\";");

            // Reverts to integer (safe only when all StallNo values are numeric).
            migrationBuilder.Sql(
                "ALTER TABLE \"Stalls\" ALTER COLUMN \"StallNo\" TYPE integer USING \"StallNo\"::integer;");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "Section", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NULL");
        }
    }
}
