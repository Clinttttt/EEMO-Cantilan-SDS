using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillingRulesCountFromMonth20261105 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CountsMonthsFrom",
                table: "BillingPolicyVersions",
                type: "date",
                nullable: false,
                // A version recorded before this column begins where the StallTrack defaults do (IA-075).
                defaultValue: new DateOnly(2026, 12, 1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsMonthsFrom",
                table: "BillingPolicyVersions");
        }
    }
}
