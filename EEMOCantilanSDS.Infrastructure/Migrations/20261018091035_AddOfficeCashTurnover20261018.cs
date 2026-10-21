using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeCashTurnover20261018 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfficeCashTurnovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoverDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TurnedOverAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DifferenceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TurnedOverBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VoidReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    VoidedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RecordedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CollectionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeCashTurnovers", x => x.Id);
                    table.UniqueConstraint("AK_OfficeCashTurnovers_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_OfficeCashTurnovers_Amounts", "\"ExpectedAmount\" > 0 AND \"TurnedOverAmount\" > 0 AND \"DifferenceAmount\" = \"ExpectedAmount\" - \"TurnedOverAmount\"");
                    table.CheckConstraint("CK_OfficeCashTurnovers_Evidence", "length(btrim(\"Reference\")) > 0 AND length(btrim(\"TurnedOverBy\")) > 0");
                    table.CheckConstraint("CK_OfficeCashTurnovers_Explained", "\"DifferenceAmount\" = 0 OR \"Remarks\" IS NOT NULL");
                    table.CheckConstraint("CK_OfficeCashTurnovers_Period", "\"PeriodTo\" >= \"PeriodFrom\" AND \"TurnoverDate\" >= \"PeriodTo\"");
                    table.CheckConstraint("CK_OfficeCashTurnovers_Status", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_OfficeCashTurnovers_Void", "(\"Status\" = 1 AND \"VoidReason\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidedByActorId\" IS NULL) OR (\"Status\" = 2 AND \"VoidReason\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND \"VoidedByActorId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_OfficeCashTurnovers_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficeCashTurnoverCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoveredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeCashTurnoverCoverages", x => x.Id);
                    table.CheckConstraint("CK_OfficeCashTurnoverCoverages_Amount", "\"CoveredAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_OfficeCashTurnoverCoverages_Collections_MunicipalityId_Coll~",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficeCashTurnoverCoverages_OfficeCashTurnovers_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.TurnoverId },
                        principalTable: "OfficeCashTurnovers",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficeCashTurnoverCoverages_MunicipalityId_CollectionId",
                table: "OfficeCashTurnoverCoverages",
                columns: new[] { "MunicipalityId", "CollectionId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_OfficeCashTurnoverCoverages_MunicipalityId_TurnoverId",
                table: "OfficeCashTurnoverCoverages",
                columns: new[] { "MunicipalityId", "TurnoverId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficeCashTurnovers_MunicipalityId_ClientOperationId",
                table: "OfficeCashTurnovers",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficeCashTurnovers_MunicipalityId_TurnoverDate",
                table: "OfficeCashTurnovers",
                columns: new[] { "MunicipalityId", "TurnoverDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficeCashTurnoverCoverages");

            migrationBuilder.DropTable(
                name: "OfficeCashTurnovers");
        }
    }
}
