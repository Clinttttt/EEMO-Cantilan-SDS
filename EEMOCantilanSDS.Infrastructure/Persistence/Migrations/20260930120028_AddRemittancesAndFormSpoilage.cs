using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemittancesAndFormSpoilage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountableFormSpoilages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustodianUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableFormSpoilages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountableFormSpoilages_AccountableDocuments_MunicipalityI~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRemittances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemittanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Instrument = table.Column<int>(type: "integer", nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemittedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DifferenceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VoidReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RecordedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CollectionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRemittances", x => x.Id);
                    table.UniqueConstraint("AK_CollectionRemittances_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_CollectionRemittances_Amounts", "\"RemittedAmount\" > 0 AND \"ExpectedAmount\" >= \"RemittedAmount\" AND \"DifferenceAmount\" = \"ExpectedAmount\" - \"RemittedAmount\"");
                    table.CheckConstraint("CK_CollectionRemittances_Period", "\"PeriodTo\" >= \"PeriodFrom\"");
                    table.CheckConstraint("CK_CollectionRemittances_Status", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_CollectionRemittances_Void", "(\"Status\" = 1 AND \"VoidReason\" IS NULL) OR (\"Status\" = 2 AND \"VoidReason\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CollectionRemittances_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRemittanceCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemittanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoveredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRemittanceCoverages", x => x.Id);
                    table.CheckConstraint("CK_CollectionRemittanceCoverages_Amount", "\"CoveredAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_CollectionRemittanceCoverages_CollectionRemittances_Municip~",
                        columns: x => new { x.MunicipalityId, x.RemittanceId },
                        principalTable: "CollectionRemittances",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionRemittanceCoverages_Collections_MunicipalityId_Co~",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountableFormSpoilages_MunicipalityId_AccountableDocument~",
                table: "AccountableFormSpoilages",
                columns: new[] { "MunicipalityId", "AccountableDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittanceCoverages_MunicipalityId_CollectionId",
                table: "CollectionRemittanceCoverages",
                columns: new[] { "MunicipalityId", "CollectionId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittanceCoverages_MunicipalityId_RemittanceId",
                table: "CollectionRemittanceCoverages",
                columns: new[] { "MunicipalityId", "RemittanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittances_MunicipalityId_ClientOperationId",
                table: "CollectionRemittances",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittances_MunicipalityId_CollectorId_Remittance~",
                table: "CollectionRemittances",
                columns: new[] { "MunicipalityId", "CollectorId", "RemittanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountableFormSpoilages");

            migrationBuilder.DropTable(
                name: "CollectionRemittanceCoverages");

            migrationBuilder.DropTable(
                name: "CollectionRemittances");
        }
    }
}
