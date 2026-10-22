using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNpmRentMonthFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Stalls_MunicipalityId_Id_FacilityId",
                table: "Stalls",
                columns: new[] { "MunicipalityId", "Id", "FacilityId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Facilities_MunicipalityId_Id",
                table: "Facilities",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateTable(
                name: "NpmRentMonths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    StallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    OwnershipAsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    AssessedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MonthBasis = table.Column<int>(type: "integer", nullable: true),
                    RateAsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    PolicyReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RateEvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreditEvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettlementVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRevisionReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReconciledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReconciledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconciliationEvidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpmRentMonths", x => x.Id);
                    table.UniqueConstraint("AK_NpmRentMonths_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_NpmRentMonths_Basis", "\"MonthBasis\" IS NULL OR \"MonthBasis\" IN (1, 2)");
                    table.CheckConstraint("CK_NpmRentMonths_Evidence", "length(btrim(\"PolicyReference\")) > 0 AND length(btrim(\"CreatedBy\")) > 0 AND jsonb_typeof(\"RateEvidenceJson\") = 'object' AND \"RateEvidenceJson\" <> '{}'::jsonb AND jsonb_typeof(\"CreditEvidenceJson\") = 'object' AND \"CreditEvidenceJson\" <> '{}'::jsonb");
                    table.CheckConstraint("CK_NpmRentMonths_Identity", "\"MunicipalityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FacilityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"StallId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ContractId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CreatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_NpmRentMonths_Money", "\"AssessedAmount\" >= 0 AND \"CreditedAmount\" >= 0 AND \"CreditedAmount\" <= \"AssessedAmount\"");
                    table.CheckConstraint("CK_NpmRentMonths_Period", "EXTRACT(DAY FROM \"PeriodStart\") = 1");
                    table.CheckConstraint("CK_NpmRentMonths_Review", "(\"Status\" = 1 AND \"ReconciledByUserId\" IS NULL AND \"ReconciledAtUtc\" IS NULL AND \"ReconciliationEvidence\" IS NULL) OR (\"Status\" = 2 AND \"MonthBasis\" IS NOT NULL AND \"ReconciledByUserId\" IS NOT NULL AND \"ReconciledByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciledAtUtc\" IS NOT NULL AND \"ReconciledAtUtc\" >= \"CreatedAtUtc\" AND \"ReconciliationEvidence\" IS NOT NULL AND length(btrim(\"ReconciliationEvidence\")) > 0)");
                    table.CheckConstraint("CK_NpmRentMonths_RevisionReason", "(\"UpdatedAtUtc\" IS NULL AND \"LastRevisionReason\" IS NULL) OR (\"UpdatedAtUtc\" IS NOT NULL AND \"LastRevisionReason\" IS NOT NULL AND length(btrim(\"LastRevisionReason\")) > 0)");
                    table.CheckConstraint("CK_NpmRentMonths_Updated", "(\"UpdatedAtUtc\" IS NULL AND \"UpdatedByUserId\" IS NULL AND \"UpdatedBy\" IS NULL) OR (\"UpdatedAtUtc\" IS NOT NULL AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND \"UpdatedByUserId\" IS NOT NULL AND \"UpdatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UpdatedBy\" IS NOT NULL AND length(btrim(\"UpdatedBy\")) > 0)");
                    table.CheckConstraint("CK_NpmRentMonths_Version", "\"SettlementVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_NpmRentMonths_Contracts_MunicipalityId_ContractId_StallId",
                        columns: x => new { x.MunicipalityId, x.ContractId, x.StallId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "MunicipalityId", "Id", "StallId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NpmRentMonths_Facilities_MunicipalityId_FacilityId",
                        columns: x => new { x.MunicipalityId, x.FacilityId },
                        principalTable: "Facilities",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NpmRentMonths_Stalls_MunicipalityId_StallId_FacilityId",
                        columns: x => new { x.MunicipalityId, x.StallId, x.FacilityId },
                        principalTable: "Stalls",
                        principalColumns: new[] { "MunicipalityId", "Id", "FacilityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRentMonths_MunicipalityId_ContractId_StallId",
                table: "NpmRentMonths",
                columns: new[] { "MunicipalityId", "ContractId", "StallId" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRentMonths_MunicipalityId_FacilityId",
                table: "NpmRentMonths",
                columns: new[] { "MunicipalityId", "FacilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRentMonths_MunicipalityId_StallId_FacilityId",
                table: "NpmRentMonths",
                columns: new[] { "MunicipalityId", "StallId", "FacilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart",
                table: "NpmRentMonths",
                columns: new[] { "MunicipalityId", "StallId", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Forward-only NPM evidence schema. An approved forward recovery plan is required.");
        }
    }
}
