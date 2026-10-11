using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FineAssessmentRelatedPeriodEvidence20261010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedObligationPeriodId",
                table: "FineAssessments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RelatedPeriodEnd",
                table: "FineAssessments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedPeriodKind",
                table: "FineAssessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedPeriodLabel",
                table: "FineAssessments",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RelatedPeriodStart",
                table: "FineAssessments",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_RelatedObligationPeriodId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "RelatedObligationPeriodId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FineAssessment_RelatedPeriod",
                table: "FineAssessments",
                sql: "(\"RelatedPeriodKind\" IS NULL AND \"RelatedPeriodStart\" IS NULL AND \"RelatedPeriodEnd\" IS NULL AND \"RelatedObligationPeriodId\" IS NULL AND \"RelatedPeriodLabel\" IS NULL) OR (\"RelatedPeriodKind\" IS NOT NULL AND \"RelatedPeriodKind\" IN (1,2,3) AND \"RelatedPeriodStart\" IS NOT NULL AND \"RelatedPeriodEnd\" IS NOT NULL AND \"RelatedPeriodEnd\" >= \"RelatedPeriodStart\" AND \"RelatedPeriodLabel\" IS NOT NULL AND ((\"RelatedPeriodKind\" = 1 AND EXTRACT(DAY FROM \"RelatedPeriodStart\") = 1 AND \"RelatedPeriodEnd\" = (\"RelatedPeriodStart\" + INTERVAL '1 month - 1 day')::date) OR (\"RelatedPeriodKind\" IN (2,3) AND \"RelatedPeriodStart\" = \"RelatedPeriodEnd\")) AND ((\"RelatedPeriodKind\" = 3 AND \"RelatedObligationPeriodId\" IS NOT NULL AND \"ObligationAccountId\" IS NOT NULL) OR (\"RelatedPeriodKind\" IN (1,2) AND \"RelatedObligationPeriodId\" IS NULL AND \"ContractId\" IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "FK_FineAssessments_ObligationPeriods_MunicipalityId_RelatedObl~",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "RelatedObligationPeriodId" },
                principalTable: "ObligationPeriods",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FineAssessments_ObligationPeriods_MunicipalityId_RelatedObl~",
                table: "FineAssessments");

            migrationBuilder.DropIndex(
                name: "IX_FineAssessments_MunicipalityId_RelatedObligationPeriodId",
                table: "FineAssessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FineAssessment_RelatedPeriod",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "RelatedObligationPeriodId",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "RelatedPeriodEnd",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "RelatedPeriodKind",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "RelatedPeriodLabel",
                table: "FineAssessments");

            migrationBuilder.DropColumn(
                name: "RelatedPeriodStart",
                table: "FineAssessments");
        }
    }
}
