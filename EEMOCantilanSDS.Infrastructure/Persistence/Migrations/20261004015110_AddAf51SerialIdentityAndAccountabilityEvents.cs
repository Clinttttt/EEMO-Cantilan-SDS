using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAf51SerialIdentityAndAccountabilityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormVariant",
                table: "AccountableFormBooks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NumberSuffix",
                table: "AccountableFormBooks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReceivedOn",
                table: "AccountableFormBooks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAuthority",
                table: "AccountableFormBooks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "AccountableFormBooks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNumber",
                table: "AccountableDocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccountableFormLossReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustodianUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CopiesLost = table.Column<int>(type: "integer", nullable: false),
                    LostOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Place = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Narrative = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReportedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    BlockedFromIssue = table.Column<bool>(type: "boolean", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableFormLossReports", x => x.Id);
                    table.CheckConstraint("CK_AccountableFormLossReports_Copies", "\"CopiesLost\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_AccountableFormLossReports_AccountableDocuments_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountableFormReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountableDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountableFormReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountableFormReferences_AccountableDocuments_Municipality~",
                        columns: x => new { x.MunicipalityId, x.AccountableDocumentId },
                        principalTable: "AccountableDocuments",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Deterministic, non-destructive backfill of the lookup key from each document's own printed number, BEFORE the
            // unique index exists. The printed DocumentNumber is untouched and stays authoritative.
            migrationBuilder.Sql(
                "UPDATE \"AccountableDocuments\" SET \"NormalizedNumber\" = upper(regexp_replace(\"DocumentNumber\", '\\s', '', 'g'));");

            migrationBuilder.CreateIndex(
                name: "IX_AccountableDocuments_MunicipalityId_InstrumentType_Normaliz~",
                table: "AccountableDocuments",
                columns: new[] { "MunicipalityId", "InstrumentType", "NormalizedNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountableFormLossReports_MunicipalityId_AccountableDocume~",
                table: "AccountableFormLossReports",
                columns: new[] { "MunicipalityId", "AccountableDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountableFormReferences_MunicipalityId_AccountableDocumen~",
                table: "AccountableFormReferences",
                columns: new[] { "MunicipalityId", "AccountableDocumentId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountableFormLossReports");

            migrationBuilder.DropTable(
                name: "AccountableFormReferences");

            migrationBuilder.DropIndex(
                name: "IX_AccountableDocuments_MunicipalityId_InstrumentType_Normaliz~",
                table: "AccountableDocuments");

            migrationBuilder.DropColumn(
                name: "FormVariant",
                table: "AccountableFormBooks");

            migrationBuilder.DropColumn(
                name: "NumberSuffix",
                table: "AccountableFormBooks");

            migrationBuilder.DropColumn(
                name: "ReceivedOn",
                table: "AccountableFormBooks");

            migrationBuilder.DropColumn(
                name: "SourceAuthority",
                table: "AccountableFormBooks");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "AccountableFormBooks");

            migrationBuilder.DropColumn(
                name: "NormalizedNumber",
                table: "AccountableDocuments");
        }
    }
}
