using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportGovernanceAndTransportationQuickAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "QuickAmountEnabled",
                table: "GovernedServiceSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OfficialReportRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RowKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SystemAmountAtRevision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SourceOrReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialReportRevisions", x => x.Id);
                    table.CheckConstraint("CK_OfficialReportRevision_Scope", "\"Year\" BETWEEN 2000 AND 2200 AND \"Revision\" > 0 AND ((\"Kind\" = 1 AND \"Month\" = 0 AND \"Amount\" >= 0) OR (\"Kind\" = 2 AND \"Month\" BETWEEN 1 AND 12 AND \"SystemAmountAtRevision\" IS NOT NULL))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialReportRevisions_MunicipalityId_ClientOperationId",
                table: "OfficialReportRevisions",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialReportRevisions_MunicipalityId_Kind_RowKey_Year_Mon~",
                table: "OfficialReportRevisions",
                columns: new[] { "MunicipalityId", "Kind", "RowKey", "Year", "Month", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficialReportRevisions");

            migrationBuilder.DropColumn(
                name: "QuickAmountEnabled",
                table: "GovernedServiceSettings");
        }
    }
}
