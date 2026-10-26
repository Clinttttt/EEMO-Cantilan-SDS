using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceContractTermMonths20261026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SpaceContractTerms_Years",
                table: "SpaceContractTerms");

            migrationBuilder.AddColumn<int>(
                name: "Months",
                table: "SpaceContractTerms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractMonths",
                table: "ObligationAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpaceContractTerms_Years",
                table: "SpaceContractTerms",
                sql: "\"Years\" BETWEEN 0 AND 50 AND (\"Years\" >= 1 OR \"Months\" IS NOT NULL) AND (\"Months\" IS NULL OR \"Months\" BETWEEN 1 AND 11)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SpaceContractTerms_Years",
                table: "SpaceContractTerms");

            migrationBuilder.DropColumn(
                name: "Months",
                table: "SpaceContractTerms");

            migrationBuilder.DropColumn(
                name: "ContractMonths",
                table: "ObligationAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpaceContractTerms_Years",
                table: "SpaceContractTerms",
                sql: "\"Years\" BETWEEN 1 AND 50");
        }
    }
}
