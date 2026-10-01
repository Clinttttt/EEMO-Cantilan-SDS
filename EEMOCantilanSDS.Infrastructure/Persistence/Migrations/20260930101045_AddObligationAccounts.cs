using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddObligationAccounts : Migration
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

            migrationBuilder.CreateTable(
                name: "ObligationAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PayorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StallId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Event = table.Column<int>(type: "integer", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObligationAccounts", x => x.Id);
                    table.UniqueConstraint("AK_ObligationAccounts_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_ObligationAccounts_Kind", "\"Kind\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_ObligationAccounts_Shape", "((\"Kind\" = 1 AND \"StallId\" IS NOT NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" = 2 AND \"StallId\" IS NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) OR (\"Kind\" = 3 AND \"StallId\" IS NULL AND \"Event\" IN (1, 2) AND \"EventDate\" IS NOT NULL))");
                    table.CheckConstraint("CK_ObligationAccounts_Window", "\"ActiveTo\" IS NULL OR \"ActiveTo\" >= \"ActiveFrom\"");
                    table.ForeignKey(
                        name: "FK_ObligationAccounts_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ObligationAccounts_Payors_MunicipalityId_PayorId",
                        columns: x => new { x.MunicipalityId, x.PayorId },
                        principalTable: "Payors",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ObligationRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObligationRates", x => x.Id);
                    table.UniqueConstraint("AK_ObligationRates_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_ObligationRates_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ObligationRates_ObligationAccounts_MunicipalityId_Obligatio~",
                        columns: x => new { x.MunicipalityId, x.ObligationAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ObligationPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    AssessedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObligationRateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObligationPeriods", x => x.Id);
                    table.UniqueConstraint("AK_ObligationPeriods_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_ObligationPeriods_Assessed", "\"AssessedAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_ObligationPeriods_ObligationAccounts_MunicipalityId_Obligat~",
                        columns: x => new { x.MunicipalityId, x.ObligationAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ObligationPeriods_ObligationRates_MunicipalityId_Obligation~",
                        columns: x => new { x.MunicipalityId, x.ObligationRateId },
                        principalTable: "ObligationRates",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

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
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10) AND \"SourcePart\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_ObligationAccounts_MunicipalityId_Kind_PayorId",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "Kind", "PayorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationAccounts_MunicipalityId_PayorId",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "PayorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationAccounts_MunicipalityId_StallId",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "StallId" },
                unique: true,
                filter: "\"Kind\" = 1 AND \"ActiveTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ObligationPeriods_MunicipalityId_ObligationAccountId_Period~",
                table: "ObligationPeriods",
                columns: new[] { "MunicipalityId", "ObligationAccountId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObligationPeriods_MunicipalityId_ObligationRateId",
                table: "ObligationPeriods",
                columns: new[] { "MunicipalityId", "ObligationRateId" });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationRates_MunicipalityId_ObligationAccountId_Effectiv~",
                table: "ObligationRates",
                columns: new[] { "MunicipalityId", "ObligationAccountId", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObligationPeriods");

            migrationBuilder.DropTable(
                name: "ObligationRates");

            migrationBuilder.DropTable(
                name: "ObligationAccounts");

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
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 9) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebCollectionDraftAllocations_SourceShape",
                table: "WebCollectionDraftAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionLines_SourceShape",
                table: "CollectionLines",
                sql: "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionAllocations_SourceShape",
                table: "CollectionAllocations",
                sql: "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
        }
    }
}
