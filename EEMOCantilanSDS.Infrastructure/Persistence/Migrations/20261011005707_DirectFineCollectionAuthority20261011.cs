using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectFineCollectionAuthority20261011 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.AddColumn<Guid>(
                name: "DirectFineAuthorityId",
                table: "CollectionLines",
                type: "uuid",
                nullable: true,
                computedColumnSql: "CASE WHEN \"SourceKind\" = 16 THEN \"SourceId\" ELSE NULL END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "DirectFineAuthorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoverningReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApprovalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AllowsUncappedAmount = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresPayerParticulars = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresReason = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresSupportingReference = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectFineAuthorities", x => x.Id);
                    table.UniqueConstraint("AK_DirectFineAuthorities_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_DirectFineAuthority_Dates", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_DirectFineAuthorities_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectFineAuthorities_PenaltyDefinitions_MunicipalityId_Pen~",
                        columns: x => new { x.MunicipalityId, x.PenaltyVersionId },
                        principalTable: "PenaltyDefinitions",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLines_MunicipalityId_DirectFineAuthorityId",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "DirectFineAuthorityId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_DirectFineAuthorities_MunicipalityId_PenaltyVersionId",
                table: "DirectFineAuthorities",
                columns: new[] { "MunicipalityId", "PenaltyVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionLines_DirectFineAuthorities_MunicipalityId_Direct~",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "DirectFineAuthorityId" },
                principalTable: "DirectFineAuthorities",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionLines_DirectFineAuthorities_MunicipalityId_Direct~",
                table: "CollectionLines");

            migrationBuilder.DropTable(
                name: "DirectFineAuthorities");

            migrationBuilder.DropIndex(
                name: "IX_CollectionLines_MunicipalityId_DirectFineAuthorityId",
                table: "CollectionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "DirectFineAuthorityId",
                table: "CollectionLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        }
    }
}
