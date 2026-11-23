using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LateChargesSurchargeAndInterest20261124 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InterestMaxMonths",
                table: "BillingPolicyVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestPercent",
                table: "BillingPolicyVersions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterestMaxMonths",
                table: "BillingPolicyVersions");

            migrationBuilder.DropColumn(
                name: "InterestPercent",
                table: "BillingPolicyVersions");
        }
    }
}
