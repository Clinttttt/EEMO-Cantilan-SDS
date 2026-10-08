using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VegetableFruitMonthlySpaceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_Kind",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_Shape",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_Kind",
                table: "ObligationAccounts",
                sql: "\"Kind\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_Shape",
                table: "ObligationAccounts",
                sql: "((\"Kind\" = 1 AND \"StallId\" IS NOT NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" IN (2, 4) AND \"StallId\" IS NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" = 3 AND \"StallId\" IS NULL AND \"Event\" IN (1, 2) AND \"EventDate\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts",
                sql: "\"PayorId\" IS NOT NULL OR (\"Kind\" IN (2, 3, 4) AND \"ActualOccupant\" IS NOT NULL AND length(trim(\"ActualOccupant\")) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_Kind",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_Shape",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_Kind",
                table: "ObligationAccounts",
                sql: "\"Kind\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_Shape",
                table: "ObligationAccounts",
                sql: "((\"Kind\" = 1 AND \"StallId\" IS NOT NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" = 2 AND \"StallId\" IS NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" = 3 AND \"StallId\" IS NULL AND \"Event\" IN (1, 2) AND \"EventDate\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts",
                sql: "\"PayorId\" IS NOT NULL OR (\"Kind\" IN (2, 3) AND \"ActualOccupant\" IS NOT NULL AND length(trim(\"ActualOccupant\")) > 0)");
        }
    }
}
