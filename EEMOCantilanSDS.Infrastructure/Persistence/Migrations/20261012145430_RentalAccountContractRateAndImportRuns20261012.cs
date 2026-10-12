using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RentalAccountContractRateAndImportRuns20261012 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ContractMonthlyRate",
                table: "ObligationAccounts",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpaceHolderImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceHolderImportRuns", x => x.Id);
                    table.CheckConstraint("CK_SpaceHolderImportRun_Counts", "\"RowCount\" >= 1 AND \"ImportedCount\" >= 0 AND \"SkippedCount\" >= 0 AND \"ImportedCount\" + \"SkippedCount\" <= \"RowCount\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceHolderImportRuns_MunicipalityId_Kind_OccurredAtUtc",
                table: "SpaceHolderImportRuns",
                columns: new[] { "MunicipalityId", "Kind", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpaceHolderImportRuns");

            migrationBuilder.DropColumn(
                name: "ContractMonthlyRate",
                table: "ObligationAccounts");
        }
    }
}
