using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FineAssessmentPolicyIntegrity20261010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FineAssessments_FineAssessmentPolicies_MunicipalityId_Polic~",
                table: "FineAssessments");

            migrationBuilder.DropIndex(
                name: "IX_FineAssessments_MunicipalityId_PolicyId",
                table: "FineAssessments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FineAssessmentPolicies_MunicipalityId_Id_PenaltyVersionId_A~",
                table: "FineAssessmentPolicies",
                columns: new[] { "MunicipalityId", "Id", "PenaltyVersionId", "AccountKind" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_PolicyId_PenaltyVersionId_Ac~",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PolicyId", "PenaltyVersionId", "AccountKind" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinePolicy_DateBasis",
                table: "FineAssessmentPolicies",
                sql: "\"DateBasis\" IN (1,2)");

            migrationBuilder.AddForeignKey(
                name: "FK_FineAssessments_FineAssessmentPolicies_MunicipalityId_Polic~",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PolicyId", "PenaltyVersionId", "AccountKind" },
                principalTable: "FineAssessmentPolicies",
                principalColumns: new[] { "MunicipalityId", "Id", "PenaltyVersionId", "AccountKind" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FineAssessments_FineAssessmentPolicies_MunicipalityId_Polic~",
                table: "FineAssessments");

            migrationBuilder.DropIndex(
                name: "IX_FineAssessments_MunicipalityId_PolicyId_PenaltyVersionId_Ac~",
                table: "FineAssessments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FineAssessmentPolicies_MunicipalityId_Id_PenaltyVersionId_A~",
                table: "FineAssessmentPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinePolicy_DateBasis",
                table: "FineAssessmentPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_PolicyId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FineAssessments_FineAssessmentPolicies_MunicipalityId_Polic~",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PolicyId" },
                principalTable: "FineAssessmentPolicies",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
