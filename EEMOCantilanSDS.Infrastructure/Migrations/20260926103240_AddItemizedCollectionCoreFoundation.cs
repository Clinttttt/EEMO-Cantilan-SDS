using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemizedCollectionCoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_ClientOperationId",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_ClientOperationId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_Collections_ClientOperationId",
                table: "Collections");

            migrationBuilder.AddColumn<int>(
                name: "ElectricitySettlementAuthorityState",
                table: "UtilityBills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ElectricitySettlementCutoverId",
                table: "UtilityBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaterSettlementAuthorityState",
                table: "UtilityBills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WaterSettlementCutoverId",
                table: "UtilityBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SettlementAuthorityState",
                table: "PaymentRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SettlementCutoverId",
                table: "PaymentRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayorId",
                table: "Contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayorId",
                table: "Collections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationSnapshot",
                table: "CollectionLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CollectionLines_MunicipalityId_Id",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateTable(
                name: "AccountableFormBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentType = table.Column<int>(type: "integer", nullable: false),
                    SeriesName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NumberPrefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FirstSerialNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastSerialNumber = table.Column<long>(type: "bigint", nullable: false),
                    SerialWidth = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableFormBooks", x => x.Id);
                    table.UniqueConstraint("AK_AccountableFormBooks_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_AccountableFormBooks_Range", "\"FirstSerialNumber\" >= 0 AND \"LastSerialNumber\" >= \"FirstSerialNumber\" AND \"SerialWidth\" BETWEEN 1 AND 30");
                });

            migrationBuilder.CreateTable(
                name: "CollectionAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePart = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceSnapshot = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionAllocations", x => x.Id);
                    table.UniqueConstraint("AK_CollectionAllocations_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_CollectionAllocations_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CollectionAllocations_SourceShape", "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_CollectionAllocations_CollectionLines_MunicipalityId_Collec~",
                        columns: x => new { x.MunicipalityId, x.CollectionLineId },
                        principalTable: "CollectionLines",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionSettlementCutovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePart = table.Column<int>(type: "integer", nullable: true),
                    BoundaryVersion = table.Column<long>(type: "bigint", nullable: false),
                    CutoverAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpeningAssessmentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningLegacySettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningOutstandingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReconciliationEvidence = table.Column<string>(type: "text", nullable: false),
                    ReconciledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionSettlementCutovers", x => x.Id);
                    table.UniqueConstraint("AK_CollectionSettlementCutovers_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_CollectionSettlementCutovers_BoundaryVersion_Positive", "\"BoundaryVersion\" > 0");
                    table.CheckConstraint("CK_CollectionSettlementCutovers_OpeningAmounts", "\"OpeningAssessmentAmount\" >= 0 AND \"OpeningLegacySettledAmount\" >= 0 AND \"OpeningOutstandingAmount\" >= 0 AND \"OpeningAssessmentAmount\" = \"OpeningLegacySettledAmount\" + \"OpeningOutstandingAmount\"");
                });

            migrationBuilder.CreateTable(
                name: "Payors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payors", x => x.Id);
                    table.UniqueConstraint("AK_Payors_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "AccountableDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentType = table.Column<int>(type: "integer", nullable: false),
                    SerialNumber = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableDocuments", x => x.Id);
                    table.UniqueConstraint("AK_AccountableDocuments_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccountableDocuments_AccountableFormBooks_MunicipalityId_Fo~",
                        columns: x => new { x.MunicipalityId, x.FormBookId },
                        principalTable: "AccountableFormBooks",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountableDocuments_Collections_MunicipalityId_CollectionId",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountableFormAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnedByActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableFormAssignments", x => x.Id);
                    table.UniqueConstraint("AK_AccountableFormAssignments_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccountableFormAssignments_AccountableDocuments_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalCollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacementDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacementCollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionType = table.Column<int>(type: "integer", nullable: false),
                    CorrectionEffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinancialEffectAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCorrections", x => x.Id);
                    table.UniqueConstraint("AK_CollectionCorrections_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.ForeignKey(
                        name: "FK_CollectionCorrections_AccountableDocuments_MunicipalityId_O~",
                        columns: x => new { x.MunicipalityId, x.OriginalDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCorrections_AccountableDocuments_MunicipalityId_R~",
                        columns: x => new { x.MunicipalityId, x.ReplacementDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCorrections_Collections_MunicipalityId_OriginalCo~",
                        columns: x => new { x.MunicipalityId, x.OriginalCollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCorrections_Collections_MunicipalityId_Replacemen~",
                        columns: x => new { x.MunicipalityId, x.ReplacementCollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostingOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentVersion = table.Column<int>(type: "integer", nullable: false),
                    NormalizedIntent = table.Column<string>(type: "text", nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OutcomeCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OutcomeDetails = table.Column<string>(type: "text", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingOperations", x => x.Id);
                    table.UniqueConstraint("AK_PostingOperations_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.ForeignKey(
                        name: "FK_PostingOperations_AccountableDocuments_MunicipalityId_Accou~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostingOperations_Collections_MunicipalityId_CollectionId",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebCollectionDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayorId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayerNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InstrumentFamily = table.Column<int>(type: "integer", nullable: true),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedRevision = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebCollectionDrafts", x => x.Id);
                    table.UniqueConstraint("AK_WebCollectionDrafts_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_WebCollectionDrafts_Revision_Positive", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_WebCollectionDrafts_AccountableDocuments_MunicipalityId_Acc~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebCollectionDrafts_Collections_MunicipalityId_CollectionId",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebCollectionDrafts_Payors_MunicipalityId_PayorId",
                        columns: x => new { x.MunicipalityId, x.PayorId },
                        principalTable: "Payors",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCorrectionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalCollectionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialEffectAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCorrectionLines", x => x.Id);
                    table.UniqueConstraint("AK_CollectionCorrectionLines_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_CollectionCorrectionLines_Effect_NonZero", "\"FinancialEffectAmount\" <> 0");
                    table.ForeignKey(
                        name: "FK_CollectionCorrectionLines_CollectionCorrections_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.CorrectionId },
                        principalTable: "CollectionCorrections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCorrectionLines_CollectionLines_MunicipalityId_Or~",
                        columns: x => new { x.MunicipalityId, x.OriginalCollectionLineId },
                        principalTable: "CollectionLines",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebCollectionDraftLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineOrder = table.Column<int>(type: "integer", nullable: false),
                    RevenueClassificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueClassificationPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SourceKind = table.Column<int>(type: "integer", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePart = table.Column<int>(type: "integer", nullable: true),
                    CalculationSnapshot = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebCollectionDraftLines", x => x.Id);
                    table.UniqueConstraint("AK_WebCollectionDraftLines_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_WebCollectionDraftLines_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_WebCollectionDraftLines_SourceShape", "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_WebCollectionDraftLines_RevenueClassificationPolicies_Munic~",
                        columns: x => new { x.MunicipalityId, x.RevenueClassificationId, x.RevenueClassificationPolicyId },
                        principalTable: "RevenueClassificationPolicies",
                        principalColumns: new[] { "MunicipalityId", "RevenueClassificationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebCollectionDraftLines_RevenueClassifications_Municipality~",
                        columns: x => new { x.MunicipalityId, x.RevenueClassificationId },
                        principalTable: "RevenueClassifications",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebCollectionDraftLines_WebCollectionDrafts_MunicipalityId_~",
                        columns: x => new { x.MunicipalityId, x.DraftId },
                        principalTable: "WebCollectionDrafts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCorrectionAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialEffectAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCorrectionAllocations", x => x.Id);
                    table.UniqueConstraint("AK_CollectionCorrectionAllocations_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_CollectionCorrectionAllocations_Effect_NonZero", "\"FinancialEffectAmount\" <> 0");
                    table.ForeignKey(
                        name: "FK_CollectionCorrectionAllocations_CollectionAllocations_Munic~",
                        columns: x => new { x.MunicipalityId, x.OriginalAllocationId },
                        principalTable: "CollectionAllocations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCorrectionAllocations_CollectionCorrectionLines_M~",
                        columns: x => new { x.MunicipalityId, x.CorrectionLineId },
                        principalTable: "CollectionCorrectionLines",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebCollectionDraftAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePart = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceSnapshot = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebCollectionDraftAllocations", x => x.Id);
                    table.UniqueConstraint("AK_WebCollectionDraftAllocations_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_WebCollectionDraftAllocations_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_WebCollectionDraftAllocations_SourceShape", "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_WebCollectionDraftAllocations_WebCollectionDraftLines_Munic~",
                        columns: x => new { x.MunicipalityId, x.DraftLineId },
                        principalTable: "WebCollectionDraftLines",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_MunicipalityId_ClientOperationId",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_MunicipalityId_ElectricitySettlementCutoverId",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "ElectricitySettlementCutoverId" });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_MunicipalityId_WaterSettlementCutoverId",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "WaterSettlementCutoverId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_MunicipalityId_ClientOperationId",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_MunicipalityId_SettlementCutoverId",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "SettlementCutoverId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_MunicipalityId_PayorId",
                table: "Contracts",
                columns: new[] { "MunicipalityId", "PayorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MunicipalityId_ClientOperationId",
                table: "Collections",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MunicipalityId_PayorId",
                table: "Collections",
                columns: new[] { "MunicipalityId", "PayorId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_ClientOperationId",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_CollectionId",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_FormBookId_SerialNumber",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "FormBookId", "SerialNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_InstrumentType_Document~",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "InstrumentType", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_State_AssignedUserId",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "State", "AssignedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountableFormAssignments_MunicipalityId_AccountableDocume~",
                table: "AccountableFormAssignments",
                columns: new[] { "MunicipalityId", "AccountableDocumentId" },
                unique: true,
                filter: "\"ReturnedAtUtc\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_MunicipalityId_CollectionLineId",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "CollectionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId_So~",
                table: "CollectionAllocations",
                columns: new[] { "MunicipalityId", "SourceKind", "SourceId", "SourcePart" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrectionAllocations_MunicipalityId_CorrectionLi~",
                table: "CollectionCorrectionAllocations",
                columns: new[] { "MunicipalityId", "CorrectionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrectionAllocations_MunicipalityId_OriginalAllo~",
                table: "CollectionCorrectionAllocations",
                columns: new[] { "MunicipalityId", "OriginalAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrectionLines_MunicipalityId_CorrectionId",
                table: "CollectionCorrectionLines",
                columns: new[] { "MunicipalityId", "CorrectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrectionLines_MunicipalityId_OriginalCollection~",
                table: "CollectionCorrectionLines",
                columns: new[] { "MunicipalityId", "OriginalCollectionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrections_MunicipalityId_OriginalCollectionId_R~",
                table: "CollectionCorrections",
                columns: new[] { "MunicipalityId", "OriginalCollectionId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrections_MunicipalityId_OriginalDocumentId",
                table: "CollectionCorrections",
                columns: new[] { "MunicipalityId", "OriginalDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrections_MunicipalityId_ReplacementCollectionId",
                table: "CollectionCorrections",
                columns: new[] { "MunicipalityId", "ReplacementCollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCorrections_MunicipalityId_ReplacementDocumentId",
                table: "CollectionCorrections",
                columns: new[] { "MunicipalityId", "ReplacementDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionSettlementCutovers_MunicipalityId_SourceKind_Sou~1",
                table: "CollectionSettlementCutovers",
                columns: new[] { "MunicipalityId", "SourceKind", "SourceId", "SourcePart" },
                unique: true,
                filter: "\"SourcePart\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionSettlementCutovers_MunicipalityId_SourceKind_Sour~",
                table: "CollectionSettlementCutovers",
                columns: new[] { "MunicipalityId", "SourceKind", "SourceId" },
                unique: true,
                filter: "\"SourcePart\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payors_MunicipalityId_DisplayName",
                table: "Payors",
                columns: new[] { "MunicipalityId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_PostingOperations_MunicipalityId_AccountableDocumentId",
                table: "PostingOperations",
                columns: new[] { "MunicipalityId", "AccountableDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostingOperations_MunicipalityId_ClientOperationId",
                table: "PostingOperations",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingOperations_MunicipalityId_CollectionId",
                table: "PostingOperations",
                columns: new[] { "MunicipalityId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDraftAllocations_MunicipalityId_DraftLineId",
                table: "WebCollectionDraftAllocations",
                columns: new[] { "MunicipalityId", "DraftLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDraftLines_MunicipalityId_DraftId_LineOrder",
                table: "WebCollectionDraftLines",
                columns: new[] { "MunicipalityId", "DraftId", "LineOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDraftLines_MunicipalityId_RevenueClassificatio~",
                table: "WebCollectionDraftLines",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "RevenueClassificationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDrafts_MunicipalityId_AccountableDocumentId",
                table: "WebCollectionDrafts",
                columns: new[] { "MunicipalityId", "AccountableDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDrafts_MunicipalityId_CollectionId",
                table: "WebCollectionDrafts",
                columns: new[] { "MunicipalityId", "CollectionId" },
                unique: true,
                filter: "\"CollectionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDrafts_MunicipalityId_OwnerUserId_Status",
                table: "WebCollectionDrafts",
                columns: new[] { "MunicipalityId", "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WebCollectionDrafts_MunicipalityId_PayorId",
                table: "WebCollectionDrafts",
                columns: new[] { "MunicipalityId", "PayorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_Payors_MunicipalityId_PayorId",
                table: "Collections",
                columns: new[] { "MunicipalityId", "PayorId" },
                principalTable: "Payors",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Payors_MunicipalityId_PayorId",
                table: "Contracts",
                columns: new[] { "MunicipalityId", "PayorId" },
                principalTable: "Payors",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRecords_CollectionSettlementCutovers_MunicipalityId_~",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "SettlementCutoverId" },
                principalTable: "CollectionSettlementCutovers",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityBills_CollectionSettlementCutovers_MunicipalityId_El~",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "ElectricitySettlementCutoverId" },
                principalTable: "CollectionSettlementCutovers",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityBills_CollectionSettlementCutovers_MunicipalityId_Wa~",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "WaterSettlementCutoverId" },
                principalTable: "CollectionSettlementCutovers",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_Payors_MunicipalityId_PayorId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Payors_MunicipalityId_PayorId",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRecords_CollectionSettlementCutovers_MunicipalityId_~",
                table: "PaymentRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_UtilityBills_CollectionSettlementCutovers_MunicipalityId_El~",
                table: "UtilityBills");

            migrationBuilder.DropForeignKey(
                name: "FK_UtilityBills_CollectionSettlementCutovers_MunicipalityId_Wa~",
                table: "UtilityBills");

            migrationBuilder.DropTable(
                name: "AccountableFormAssignments");

            migrationBuilder.DropTable(
                name: "CollectionCorrectionAllocations");

            migrationBuilder.DropTable(
                name: "CollectionSettlementCutovers");

            migrationBuilder.DropTable(
                name: "PostingOperations");

            migrationBuilder.DropTable(
                name: "WebCollectionDraftAllocations");

            migrationBuilder.DropTable(
                name: "CollectionAllocations");

            migrationBuilder.DropTable(
                name: "CollectionCorrectionLines");

            migrationBuilder.DropTable(
                name: "WebCollectionDraftLines");

            migrationBuilder.DropTable(
                name: "CollectionCorrections");

            migrationBuilder.DropTable(
                name: "WebCollectionDrafts");

            migrationBuilder.DropTable(
                name: "AccountableDocuments");

            migrationBuilder.DropTable(
                name: "Payors");

            migrationBuilder.DropTable(
                name: "AccountableFormBooks");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_MunicipalityId_ClientOperationId",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_MunicipalityId_ElectricitySettlementCutoverId",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_MunicipalityId_WaterSettlementCutoverId",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_MunicipalityId_ClientOperationId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_MunicipalityId_SettlementCutoverId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_MunicipalityId_PayorId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Collections_MunicipalityId_ClientOperationId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_MunicipalityId_PayorId",
                table: "Collections");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CollectionLines_MunicipalityId_Id",
                table: "CollectionLines");

            migrationBuilder.DropColumn(
                name: "ElectricitySettlementAuthorityState",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "ElectricitySettlementCutoverId",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "WaterSettlementAuthorityState",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "WaterSettlementCutoverId",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "SettlementAuthorityState",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "SettlementCutoverId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PayorId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PayorId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "CalculationSnapshot",
                table: "CollectionLines");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ClientOperationId",
                table: "UtilityBills",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_ClientOperationId",
                table: "PaymentRecords",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_ClientOperationId",
                table: "Collections",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");
        }
    }
}
