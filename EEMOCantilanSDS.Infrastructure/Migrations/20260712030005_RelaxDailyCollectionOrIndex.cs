using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelaxDailyCollectionOrIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections",
                columns: new[] { "MunicipalityId", "ORNumber" },
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }
    }
}
