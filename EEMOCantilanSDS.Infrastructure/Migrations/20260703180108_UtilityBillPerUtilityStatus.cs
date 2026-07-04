using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UtilityBillPerUtilityStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Status",
                table: "UtilityBills",
                newName: "WaterStatus");

            migrationBuilder.RenameColumn(
                name: "PartialAmount",
                table: "UtilityBills",
                newName: "WaterPartialAmount");

            migrationBuilder.AddColumn<decimal>(
                name: "ElecPartialAmount",
                table: "UtilityBills",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ElecStatus",
                table: "UtilityBills",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElecPartialAmount",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "ElecStatus",
                table: "UtilityBills");

            migrationBuilder.RenameColumn(
                name: "WaterStatus",
                table: "UtilityBills",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "WaterPartialAmount",
                table: "UtilityBills",
                newName: "PartialAmount");
        }
    }
}
