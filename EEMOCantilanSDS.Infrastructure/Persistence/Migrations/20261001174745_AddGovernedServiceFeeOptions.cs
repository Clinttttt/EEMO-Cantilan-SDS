using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedServiceFeeOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings");

            migrationBuilder.CreateTable(
                name: "GovernedServiceFeeOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    GovernedServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RetiredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedServiceFeeOptions", x => x.Id);
                    table.UniqueConstraint("AK_GovernedServiceFeeOptions_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_GovernedServiceFeeOptions_Code", "\"Code\" IS NULL OR \"Code\" ~ '^[A-Z][A-Z0-9_]{1,39}$'");
                    table.ForeignKey(
                        name: "FK_GovernedServiceFeeOptions_GovernedServices_MunicipalityId_G~",
                        columns: x => new { x.MunicipalityId, x.GovernedServiceId },
                        principalTable: "GovernedServices",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedServiceFeeOptionRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    FixedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedServiceFeeOptionRates", x => x.Id);
                    table.CheckConstraint("CK_GovernedServiceFeeOptionRates_AmountShape", "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)))");
                    table.CheckConstraint("CK_GovernedServiceFeeOptionRates_Basis", "\"Basis\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_GovernedServiceFeeOptionRates_GovernedServiceFeeOptions_Mun~",
                        columns: x => new { x.MunicipalityId, x.FeeOptionId },
                        principalTable: "GovernedServiceFeeOptions",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings",
                sql: "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)) OR (\"Basis\" IN (3, 4) AND \"FixedAmount\" IS NULL AND \"MaximumAmount\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings",
                sql: "\"Basis\" IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_GovernedServiceFeeOptionRates_MunicipalityId_FeeOptionId_Ef~",
                table: "GovernedServiceFeeOptionRates",
                columns: new[] { "MunicipalityId", "FeeOptionId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedServiceFeeOptions_MunicipalityId_GovernedServiceId_~",
                table: "GovernedServiceFeeOptions",
                columns: new[] { "MunicipalityId", "GovernedServiceId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernedServiceFeeOptionRates");

            migrationBuilder.DropTable(
                name: "GovernedServiceFeeOptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings",
                sql: "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)) OR (\"Basis\" = 3 AND \"FixedAmount\" IS NULL AND \"MaximumAmount\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings",
                sql: "\"Basis\" IN (1, 2, 3)");
        }
    }
}
