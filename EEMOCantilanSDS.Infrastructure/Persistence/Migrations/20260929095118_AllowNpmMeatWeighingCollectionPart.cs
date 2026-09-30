using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowNpmMeatWeighingCollectionPart : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
        }
    }
}
