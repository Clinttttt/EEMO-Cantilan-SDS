using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GovernedFineAssessments20261010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Contracts_MunicipalityId_Id",
                table: "Contracts",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateTable(
                name: "FineAssessmentPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountKind = table.Column<int>(type: "integer", nullable: false),
                    Facility = table.Column<int>(type: "integer", nullable: true),
                    GoverningReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DateBasis = table.Column<int>(type: "integer", nullable: false),
                    AllowsUncappedAmount = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsClosedAccount = table.Column<bool>(type: "boolean", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FineAssessmentPolicies", x => x.Id);
                    table.UniqueConstraint("AK_FineAssessmentPolicies_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_FinePolicy_Source", "(\"AccountKind\" = 1 AND \"Facility\" IS NOT NULL) OR (\"AccountKind\" IN (2,3,4) AND \"Facility\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_FineAssessmentPolicies_PenaltyDefinitions_MunicipalityId_Pe~",
                        columns: x => new { x.MunicipalityId, x.PenaltyVersionId },
                        principalTable: "PenaltyDefinitions",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FineAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountKind = table.Column<int>(type: "integer", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PenaltyName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Occupant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AuthoritySnapshot = table.Column<string>(type: "text", nullable: false),
                    ViolationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IncidentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AssessedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FineAssessments", x => x.Id);
                    table.UniqueConstraint("AK_FineAssessments_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_FineAssessment_Amount", "\"AssessedAmount\" > 0");
                    table.CheckConstraint("CK_FineAssessment_Dates", "\"ViolationDate\" <= \"BusinessDate\"");
                    table.CheckConstraint("CK_FineAssessment_Source", "(\"AccountKind\" = 1 AND \"ContractId\" IS NOT NULL AND \"ObligationAccountId\" IS NULL) OR (\"AccountKind\" IN (2,3,4) AND \"ContractId\" IS NULL AND \"ObligationAccountId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FineAssessments_Contracts_MunicipalityId_ContractId",
                        columns: x => new { x.MunicipalityId, x.ContractId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FineAssessments_FineAssessmentPolicies_MunicipalityId_Polic~",
                        columns: x => new { x.MunicipalityId, x.PolicyId },
                        principalTable: "FineAssessmentPolicies",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FineAssessments_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FineAssessments_ObligationAccounts_MunicipalityId_Obligatio~",
                        columns: x => new { x.MunicipalityId, x.ObligationAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FineAssessments_PenaltyDefinitions_MunicipalityId_PenaltyVe~",
                        columns: x => new { x.MunicipalityId, x.PenaltyVersionId },
                        principalTable: "PenaltyDefinitions",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FineAssessmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResultingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FineAssessmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FineAssessmentEvents_FineAssessments_MunicipalityId_Assessm~",
                        columns: x => new { x.MunicipalityId, x.AssessmentId },
                        principalTable: "FineAssessments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessmentEvents_MunicipalityId_AssessmentId",
                table: "FineAssessmentEvents",
                columns: new[] { "MunicipalityId", "AssessmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessmentPolicies_MunicipalityId_PenaltyVersionId",
                table: "FineAssessmentPolicies",
                columns: new[] { "MunicipalityId", "PenaltyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_BusinessDate",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_ClientOperationId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_ContractId_ObligationAccount~",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "ContractId", "ObligationAccountId", "PenaltyCode", "IncidentReference" },
                unique: true,
                filter: "\"IncidentReference\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_ObligationAccountId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "ObligationAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_PenaltyVersionId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PenaltyVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FineAssessments_MunicipalityId_PolicyId",
                table: "FineAssessments",
                columns: new[] { "MunicipalityId", "PolicyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FineAssessmentEvents");

            migrationBuilder.DropTable(
                name: "FineAssessments");

            migrationBuilder.DropTable(
                name: "FineAssessmentPolicies");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Contracts_MunicipalityId_Id",
                table: "Contracts");
        }
    }
}
