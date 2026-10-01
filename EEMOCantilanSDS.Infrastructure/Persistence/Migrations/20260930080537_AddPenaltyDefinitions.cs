using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPenaltyDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.CreateTable(
                name: "PenaltyDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    FixedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_PenaltyDefinitions_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_PenaltyDefinitions_AmountShape", "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)))");
                    table.CheckConstraint("CK_PenaltyDefinitions_Basis", "\"Basis\" IN (1, 2)");
                    table.CheckConstraint("CK_PenaltyDefinitions_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{1,39}$'");
                    table.ForeignKey(
                        name: "FK_PenaltyDefinitions_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 9) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyDefinitions_MunicipalityId_Code_EffectiveDate",
                table: "PenaltyDefinitions",
                columns: new[] { "MunicipalityId", "Code", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenaltyDefinitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftLines_SourceShape",
                table: "WebCollectionDraftLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        }
    }
}
