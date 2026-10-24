using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FilterDeletedStallsFromUniqueNumbers20261025 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "Section", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NOT NULL AND NOT \"IsDeleted\"");

            // Manually managed null-section expression index: EF's model does not track it.
            // Soft-deleted historical identities must not reserve a current tenant/section number.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\";" +
                "CREATE UNIQUE INDEX \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\" " +
                "ON \"Stalls\" (\"FacilityId\", (COALESCE(\"CustomSectionName\", '')), \"StallNo\") " +
                "WHERE \"Section\" IS NULL AND NOT \"IsDeleted\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrade to the old constraint is impossible once a soft-deleted number has
            // been legitimately reused. PostgreSQL deliberately rejects that downgrade
            // instead of silently deleting or renumbering historical records.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\";" +
                "CREATE UNIQUE INDEX \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\" " +
                "ON \"Stalls\" (\"FacilityId\", (COALESCE(\"CustomSectionName\", '')), \"StallNo\") " +
                "WHERE \"Section\" IS NULL;");
            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "Section", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NOT NULL");
        }
    }
}
