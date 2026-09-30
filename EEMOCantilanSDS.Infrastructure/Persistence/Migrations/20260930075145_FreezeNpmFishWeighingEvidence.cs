using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreezeNpmFishWeighingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FishFeeAmountFrozen",
                table: "DailyCollections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FishFeeRateEffectiveDate",
                table: "DailyCollections",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FishFeeRatePerKilo",
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
                name: "FishFeeAmountFrozen",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "FishFeeRateEffectiveDate",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "FishFeeRatePerKilo",
                table: "DailyCollections");
        }
    }
}
