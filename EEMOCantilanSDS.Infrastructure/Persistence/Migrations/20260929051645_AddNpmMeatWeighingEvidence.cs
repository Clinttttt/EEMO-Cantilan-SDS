using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNpmMeatWeighingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MeatFeeAmount",
                table: "DailyCollections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MeatFeeRateEffectiveDate",
                table: "DailyCollections",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeatFeeRatePerKilo",
                table: "DailyCollections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeatKilos",
                table: "DailyCollections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeatFeeAmount",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "MeatFeeRateEffectiveDate",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "MeatFeeRatePerKilo",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "MeatKilos",
                table: "DailyCollections");
        }
    }
}
