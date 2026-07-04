using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UtilityBillPerUtilityOrNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_ORNumber",
                table: "UtilityBills");

            migrationBuilder.RenameColumn(
                name: "ORNumber",
                table: "UtilityBills",
                newName: "WaterORNumber");

            migrationBuilder.AddColumn<string>(
                name: "ElecORNumber",
                table: "UtilityBills",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Existing bills carried one OR (now preserved in WaterORNumber). Where electricity was also
            // settled under that same receipt (status != Unpaid), copy it to the electricity OR too.
            migrationBuilder.Sql(
                "UPDATE \"UtilityBills\" SET \"ElecORNumber\" = \"WaterORNumber\" " +
                "WHERE \"ElecStatus\" <> 0 AND \"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> '' " +
                "AND (\"ElecORNumber\" IS NULL OR \"ElecORNumber\" = '');");

            // And where water was NOT settled, the preserved receipt belongs to electricity only.
            migrationBuilder.Sql(
                "UPDATE \"UtilityBills\" SET \"ElecORNumber\" = \"WaterORNumber\", \"WaterORNumber\" = NULL " +
                "WHERE \"WaterStatus\" = 0 AND \"ElecStatus\" <> 0 AND \"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> '';");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ElecORNumber",
                table: "UtilityBills",
                column: "ElecORNumber",
                unique: true,
                filter: "\"ElecORNumber\" IS NOT NULL AND \"ElecORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_WaterORNumber",
                table: "UtilityBills",
                column: "WaterORNumber",
                unique: true,
                filter: "\"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_ElecORNumber",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_WaterORNumber",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "ElecORNumber",
                table: "UtilityBills");

            migrationBuilder.RenameColumn(
                name: "WaterORNumber",
                table: "UtilityBills",
                newName: "ORNumber");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ORNumber",
                table: "UtilityBills",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }
    }
}
