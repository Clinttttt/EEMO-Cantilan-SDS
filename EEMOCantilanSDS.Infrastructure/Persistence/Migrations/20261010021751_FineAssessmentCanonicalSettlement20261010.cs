using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FineAssessmentCanonicalSettlement20261010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations");

            migrationBuilder.AddColumn<Guid>(
                name: "FineAssessmentId",
                table: "CollectionAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 9, 10, 15) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10, 15) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_MunicipalityId_FineAssessmentId",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "FineAssessmentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_FineIdentity",
                table: "CollectionAllocations",
                sql: "(\"SourceKind\" = 15 AND \"FineAssessmentId\" IS NOT NULL AND \"FineAssessmentId\" = \"SourceId\") OR (\"SourceKind\" <> 15 AND \"FineAssessmentId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10, 15) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionAllocations_FineAssessments_MunicipalityId_FineAs~",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "FineAssessmentId" },
                principalTable: "FineAssessments",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionAllocations_FineAssessments_MunicipalityId_FineAs~",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropIndex(
                name: "IX_CollectionAllocations_MunicipalityId_FineAssessmentId",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_FineIdentity",
                table: "CollectionAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "FineAssessmentId",
                table: "CollectionAllocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 9, 10) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10) AND \"SourcePart\" IS NULL))");
        }
    }
}
