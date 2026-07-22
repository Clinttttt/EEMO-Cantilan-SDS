using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceNullSectionStallUniqueIndexPerCustomSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls");

            // Replace the per-(FacilityId, StallNo) null-section unique index with a per-(FacilityId, custom
            // section, StallNo) one. COALESCE(CustomSectionName, '') keeps non-NPM facilities (CustomSectionName
            // null → '') at per-facility uniqueness EXACTLY as before, while each NPM custom section numbers its
            // stalls independently (1,2,3…). This is an expression index EF cannot model, so it is raw SQL and
            // is intentionally absent from the model snapshot (manually managed). The old index is strictly
            // stricter, so no existing row can violate the new (more permissive) one.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\" " +
                "ON \"Stalls\" (\"FacilityId\", (COALESCE(\"CustomSectionName\", '')), \"StallNo\") " +
                "WHERE \"Section\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Stalls_NullSection_Facility_CustomSection_StallNo\";");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "StallNo" },
                unique: true,
                filter: "\"Section\" IS NULL");
        }
    }
}
