using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContextualRevenueInstrumentPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies");

            migrationBuilder.AddColumn<int>(
                name: "BusinessContext",
                table: "RevenueClassificationPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "EffectiveDate", "BusinessContext" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RevenueClassificationPolicies_BusinessContext",
                table: "RevenueClassificationPolicies",
                sql: "\"BusinessContext\" IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RevenueClassificationPolicies_BusinessContext",
                table: "RevenueClassificationPolicies");

            migrationBuilder.DropColumn(
                name: "BusinessContext",
                table: "RevenueClassificationPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "EffectiveDate" },
                unique: true);
        }
    }
}
