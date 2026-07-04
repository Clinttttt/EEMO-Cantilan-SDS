using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UtilityBillPerUtilityPaidAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ElecPaidAt",
                table: "UtilityBills",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaterPaidAt",
                table: "UtilityBills",
                type: "timestamp with time zone",
                nullable: true);

            // Seed per-utility paid-at from the existing overall PaidAt for utilities already settled.
            migrationBuilder.Sql(
                "UPDATE \"UtilityBills\" SET \"ElecPaidAt\" = \"PaidAt\" WHERE \"ElecStatus\" <> 0 AND \"PaidAt\" IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE \"UtilityBills\" SET \"WaterPaidAt\" = \"PaidAt\" WHERE \"WaterStatus\" <> 0 AND \"PaidAt\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElecPaidAt",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "WaterPaidAt",
                table: "UtilityBills");
        }
    }
}
