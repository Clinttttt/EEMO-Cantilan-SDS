using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNpmRentMonthFinancialSource20261020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NpmRentMonths_Review",
                table: "NpmRentMonths");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations");

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementActivatedAtUtc",
                table: "NpmRentMonths",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SettlementActivatedByUserId",
                table: "NpmRentMonths",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementSealedAtUtc",
                table: "NpmRentMonths",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NpmRentMonthId",
                table: "CollectionAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NpmRentMonths_Activation",
                table: "NpmRentMonths",
                sql: "(\"Status\" IN (1,2) AND \"SettlementActivatedAtUtc\" IS NULL AND \"SettlementActivatedByUserId\" IS NULL) OR (\"Status\" = 3 AND \"SettlementActivatedAtUtc\" IS NOT NULL AND \"SettlementActivatedAtUtc\" >= \"ReconciledAtUtc\" AND \"SettlementActivatedByUserId\" IS NOT NULL AND \"SettlementActivatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NpmRentMonths_Review",
                table: "NpmRentMonths",
                sql: "(\"Status\" = 1 AND \"ReconciledByUserId\" IS NULL AND \"ReconciledAtUtc\" IS NULL AND \"ReconciliationEvidence\" IS NULL) OR (\"Status\" IN (2,3) AND \"MonthBasis\" IS NOT NULL AND \"ReconciledByUserId\" IS NOT NULL AND \"ReconciledByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciledAtUtc\" IS NOT NULL AND \"ReconciledAtUtc\" >= \"CreatedAtUtc\" AND \"ReconciliationEvidence\" IS NOT NULL AND length(btrim(\"ReconciliationEvidence\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NpmRentMonths_Sealed",
                table: "NpmRentMonths",
                sql: "\"SettlementSealedAtUtc\" IS NULL OR (\"Status\" = 3 AND \"SettlementSealedAtUtc\" >= \"SettlementActivatedAtUtc\" AND \"SettlementActivatedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_MunicipalityId_NpmRentMonthId",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "NpmRentMonthId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_NpmMonthIdentity",
                table: "CollectionAllocations",
                sql: "(\"SourceKind\" = 17 AND \"NpmRentMonthId\" IS NOT NULL AND \"NpmRentMonthId\" = \"SourceId\") OR (\"SourceKind\" <> 17 AND \"NpmRentMonthId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10, 15, 17) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionAllocations_NpmRentMonths_MunicipalityId_NpmRentM~",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "NpmRentMonthId" },
                principalTable: "NpmRentMonths",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never discard assessment or allocation evidence; empty disposable databases can verify rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "NpmRentMonths")
                    OR EXISTS (SELECT 1 FROM "CollectionAllocations" WHERE "NpmRentMonthId" IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM "CollectionLines" WHERE "SourceKind" = 17)
                  THEN RAISE EXCEPTION 'NPM financial evidence exists. Restore an approved full backup instead of rolling back.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionAllocations_NpmRentMonths_MunicipalityId_NpmRentM~",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NpmRentMonths_Activation",
                table: "NpmRentMonths");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NpmRentMonths_Review",
                table: "NpmRentMonths");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NpmRentMonths_Sealed",
                table: "NpmRentMonths");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropIndex(
                name: "IX_CollectionAllocations_MunicipalityId_NpmRentMonthId",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_NpmMonthIdentity",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "SettlementActivatedAtUtc",
                table: "NpmRentMonths");

            migrationBuilder.DropColumn(
                name: "SettlementActivatedByUserId",
                table: "NpmRentMonths");

            migrationBuilder.DropColumn(
                name: "SettlementSealedAtUtc",
                table: "NpmRentMonths");

            migrationBuilder.DropColumn(
                name: "NpmRentMonthId",
                table: "CollectionAllocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NpmRentMonths_Review",
                table: "NpmRentMonths",
                sql: "(\"Status\" = 1 AND \"ReconciledByUserId\" IS NULL AND \"ReconciledAtUtc\" IS NULL AND \"ReconciliationEvidence\" IS NULL) OR (\"Status\" = 2 AND \"MonthBasis\" IS NOT NULL AND \"ReconciledByUserId\" IS NOT NULL AND \"ReconciledByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciledAtUtc\" IS NOT NULL AND \"ReconciledAtUtc\" >= \"CreatedAtUtc\" AND \"ReconciliationEvidence\" IS NOT NULL AND length(btrim(\"ReconciliationEvidence\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10, 15) AND \"SourcePart\" IS NULL))");
        }
    }
}
