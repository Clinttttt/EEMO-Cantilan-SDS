using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillingPolicyVersions20261104 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDay = table.Column<int>(type: "integer", nullable: true),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    OldestMonthFirst = table.Column<bool>(type: "boolean", nullable: false),
                    MaxAdvanceMonths = table.Column<int>(type: "integer", nullable: false),
                    AnnualPaymentAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyBasis = table.Column<int>(type: "integer", nullable: false),
                    PenaltyValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PenaltyRepeatsMonthly = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyMonthCap = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    GoverningReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPolicyVersions", x => x.Id);
                    table.CheckConstraint("CK_BillingPolicy_Advance", "\"MaxAdvanceMonths\" BETWEEN 0 AND 12");
                    table.CheckConstraint("CK_BillingPolicy_DueDay", "\"DueDay\" IS NULL OR \"DueDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_BillingPolicy_Grace", "\"GraceDays\" BETWEEN 0 AND 90");
                    table.CheckConstraint("CK_BillingPolicy_Penalty", "(\"PenaltyBasis\" = 0 AND \"PenaltyValue\" IS NULL) OR (\"PenaltyBasis\" IN (1,2) AND \"PenaltyValue\" > 0 AND \"GoverningReference\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BillingPolicyVersions_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPolicyVersions_MunicipalityId_EffectiveFrom",
                table: "BillingPolicyVersions",
                columns: new[] { "MunicipalityId", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingPolicyVersions");
        }
    }
}
