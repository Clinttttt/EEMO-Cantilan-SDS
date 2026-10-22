using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceIdentityAndLifecycle20261020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorAccountId",
                table: "ObligationAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAtUtc",
                table: "ObligationAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpaceRegistrationId",
                table: "ObligationAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpaceRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DisplayNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceRegistrations", x => x.Id);
                    table.UniqueConstraint("AK_SpaceRegistrations_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_SpaceRegistrations_Kind", "\"Kind\" IN (2, 4)");
                    table.ForeignKey(
                        name: "FK_SpaceRegistrations_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpaceLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviewVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceLifecycleEvents", x => x.Id);
                    table.UniqueConstraint("AK_SpaceLifecycleEvents_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.ForeignKey(
                        name: "FK_SpaceLifecycleEvents_ObligationAccounts_MunicipalityId_Resu~",
                        columns: x => new { x.MunicipalityId, x.ResultAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceLifecycleEvents_ObligationAccounts_MunicipalityId_Sour~",
                        columns: x => new { x.MunicipalityId, x.SourceAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceLifecycleEvents_SpaceRegistrations_MunicipalityId_Spac~",
                        columns: x => new { x.MunicipalityId, x.SpaceRegistrationId },
                        principalTable: "SpaceRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpaceContractTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriorTermId = table.Column<Guid>(type: "uuid", nullable: true),
                    Commencement = table.Column<DateOnly>(type: "date", nullable: false),
                    Years = table.Column<int>(type: "integer", nullable: false),
                    ContractName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovalEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LifecycleEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceContractTerms", x => x.Id);
                    table.UniqueConstraint("AK_SpaceContractTerms_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_SpaceContractTerms_Years", "\"Years\" BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_SpaceContractTerms_ObligationAccounts_MunicipalityId_Obliga~",
                        columns: x => new { x.MunicipalityId, x.ObligationAccountId },
                        principalTable: "ObligationAccounts",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceContractTerms_SpaceContractTerms_MunicipalityId_PriorT~",
                        columns: x => new { x.MunicipalityId, x.PriorTermId },
                        principalTable: "SpaceContractTerms",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceContractTerms_SpaceLifecycleEvents_MunicipalityId_Life~",
                        columns: x => new { x.MunicipalityId, x.LifecycleEventId },
                        principalTable: "SpaceLifecycleEvents",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceContractTerms_SpaceRegistrations_MunicipalityId_SpaceR~",
                        columns: x => new { x.MunicipalityId, x.SpaceRegistrationId },
                        principalTable: "SpaceRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationAccounts_MunicipalityId_PredecessorAccountId",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "PredecessorAccountId" },
                unique: true,
                filter: "\"PredecessorAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ObligationAccounts_MunicipalityId_SpaceRegistrationId",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "SpaceRegistrationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_Retired",
                table: "ObligationAccounts",
                sql: "\"RetiredAtUtc\" IS NULL OR \"ActiveTo\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ObligationAccounts_SpaceIdentity",
                table: "ObligationAccounts",
                sql: "(\"SpaceRegistrationId\" IS NULL OR \"Kind\" IN (2, 4)) AND (\"PredecessorAccountId\" IS NULL OR (\"SpaceRegistrationId\" IS NOT NULL AND \"PredecessorAccountId\" <> \"Id\"))");

            migrationBuilder.CreateIndex(
                name: "IX_SpaceContractTerms_MunicipalityId_LifecycleEventId",
                table: "SpaceContractTerms",
                columns: new[] { "MunicipalityId", "LifecycleEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceContractTerms_MunicipalityId_ObligationAccountId_Comme~",
                table: "SpaceContractTerms",
                columns: new[] { "MunicipalityId", "ObligationAccountId", "Commencement" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceContractTerms_MunicipalityId_PriorTermId",
                table: "SpaceContractTerms",
                columns: new[] { "MunicipalityId", "PriorTermId" },
                unique: true,
                filter: "\"PriorTermId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SpaceContractTerms_MunicipalityId_SpaceRegistrationId",
                table: "SpaceContractTerms",
                columns: new[] { "MunicipalityId", "SpaceRegistrationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceLifecycleEvents_MunicipalityId_ClientOperationId",
                table: "SpaceLifecycleEvents",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpaceLifecycleEvents_MunicipalityId_ResultAccountId",
                table: "SpaceLifecycleEvents",
                columns: new[] { "MunicipalityId", "ResultAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceLifecycleEvents_MunicipalityId_SourceAccountId",
                table: "SpaceLifecycleEvents",
                columns: new[] { "MunicipalityId", "SourceAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceLifecycleEvents_MunicipalityId_SpaceRegistrationId_Rec~",
                table: "SpaceLifecycleEvents",
                columns: new[] { "MunicipalityId", "SpaceRegistrationId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceRegistrations_MunicipalityId_Kind_NormalizedNumber",
                table: "SpaceRegistrations",
                columns: new[] { "MunicipalityId", "Kind", "NormalizedNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ObligationAccounts_ObligationAccounts_MunicipalityId_Predec~",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "PredecessorAccountId" },
                principalTable: "ObligationAccounts",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ObligationAccounts_SpaceRegistrations_MunicipalityId_SpaceR~",
                table: "ObligationAccounts",
                columns: new[] { "MunicipalityId", "SpaceRegistrationId" },
                principalTable: "SpaceRegistrations",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // ── Database-level guarantees EF cannot model ────────────────────────────────────────────────────────────────────
            // Additive and data-free: every new column is NULL for existing rows, so nothing below can conflict with existing data.
            // No existing row is linked, merged or rewritten by this migration; linking is the guarded backfill's job.

            // btree_gist lets a uuid equality and a date-range overlap share one GiST exclusion constraint. On a managed server the
            // extension must be allow-listed first (see the architecture document's activation requirements).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // Two occupancies of one physical space may never share a day. Inclusive on both ends: [ActiveFrom, ActiveTo]; an open
            // account runs to infinity. Rows with no registration are not constrained (they are not yet identified, and are
            // reported by the preflight rather than guessed at).
            migrationBuilder.Sql(@"
ALTER TABLE ""ObligationAccounts"" ADD CONSTRAINT ""EX_ObligationAccounts_SpaceOccupancy""
    EXCLUDE USING gist (
        ""MunicipalityId"" WITH =,
        ""SpaceRegistrationId"" WITH =,
        daterange(""ActiveFrom"", COALESCE(""ActiveTo"" + 1, 'infinity'::date), '[)') WITH &&)
    WHERE (""SpaceRegistrationId"" IS NOT NULL);");

            // Lifecycle evidence and contract terms are append-only: the database refuses to change or remove them.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION ""SpaceLifecycleAppendOnly""() RETURNS trigger AS $$
BEGIN
    -- Only a tenant restore (which replaces the tenant's rows verbatim inside its own transaction) may delete.
    IF TG_OP = 'DELETE' AND current_setting('stalltrack.tenant_restore', true) = 'on' THEN RETURN OLD; END IF;
    RAISE EXCEPTION '% is append-only: % is not permitted.', TG_TABLE_NAME, TG_OP USING ERRCODE = '55000';
END;
$$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"
CREATE TRIGGER ""TR_SpaceLifecycleEvents_AppendOnly"" BEFORE UPDATE OR DELETE ON ""SpaceLifecycleEvents""
    FOR EACH ROW EXECUTE FUNCTION ""SpaceLifecycleAppendOnly""();");
            migrationBuilder.Sql(@"
CREATE TRIGGER ""TR_SpaceContractTerms_AppendOnly"" BEFORE UPDATE OR DELETE ON ""SpaceContractTerms""
    FOR EACH ROW EXECUTE FUNCTION ""SpaceLifecycleAppendOnly""();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse of the raw SQL above, before the tables and columns it protects are dropped. The extension is left installed: it is
            // harmless and other objects may use it.
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS ""TR_SpaceContractTerms_AppendOnly"" ON ""SpaceContractTerms"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS ""TR_SpaceLifecycleEvents_AppendOnly"" ON ""SpaceLifecycleEvents"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS ""SpaceLifecycleAppendOnly""();");
            migrationBuilder.Sql(@"ALTER TABLE ""ObligationAccounts"" DROP CONSTRAINT IF EXISTS ""EX_ObligationAccounts_SpaceOccupancy"";");

            migrationBuilder.DropForeignKey(
                name: "FK_ObligationAccounts_ObligationAccounts_MunicipalityId_Predec~",
                table: "ObligationAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_ObligationAccounts_SpaceRegistrations_MunicipalityId_SpaceR~",
                table: "ObligationAccounts");

            migrationBuilder.DropTable(
                name: "SpaceContractTerms");

            migrationBuilder.DropTable(
                name: "SpaceLifecycleEvents");

            migrationBuilder.DropTable(
                name: "SpaceRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_ObligationAccounts_MunicipalityId_PredecessorAccountId",
                table: "ObligationAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ObligationAccounts_MunicipalityId_SpaceRegistrationId",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_Retired",
                table: "ObligationAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ObligationAccounts_SpaceIdentity",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "PredecessorAccountId",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "RetiredAtUtc",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "SpaceRegistrationId",
                table: "ObligationAccounts");
        }
    }
}
