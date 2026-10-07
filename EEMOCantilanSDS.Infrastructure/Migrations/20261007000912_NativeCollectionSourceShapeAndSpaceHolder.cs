using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NativeCollectionSourceShapeAndSpaceHolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayorId",
                table: "ObligationAccounts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "ActualOccupant",
                table: "ObligationAccounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts",
                sql: "\"PayorId\" IS NOT NULL OR (\"Kind\" IN (2, 3) AND \"ActualOccupant\" IS NOT NULL AND length(trim(\"ActualOccupant\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_SourceHolder",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "ActualOccupant",
                table: "ObligationAccounts");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayorId",
                table: "ObligationAccounts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        }
    }
}
