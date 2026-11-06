using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FineAssessmentAdjustment20261105 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustedAmount",
                table: "FineAssessments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FineAssessment_Adjusted",
                table: "FineAssessments",
                sql: "\"AdjustedAmount\" IS NULL OR \"AdjustedAmount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FineAssessment_Adjusted",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "AdjustedAmount",
                table: "FineAssessments");
        }
    }
}
