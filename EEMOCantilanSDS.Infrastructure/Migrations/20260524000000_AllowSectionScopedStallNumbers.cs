using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowSectionScopedStallNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls");

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
            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_Section_StallNo",
                table: "Stalls");

            migrationBuilder.DropIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls");

            migrationBuilder.CreateIndex(
                name: "IX_Stalls_FacilityId_StallNo",
                table: "Stalls",
                columns: new[] { "FacilityId", "StallNo" },
                unique: true);
        }
    }
}
