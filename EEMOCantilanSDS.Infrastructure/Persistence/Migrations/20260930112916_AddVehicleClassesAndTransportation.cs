using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleClassesAndTransportation : Migration
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
                name: "VehicleClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleClasses", x => x.Id);
                    table.UniqueConstraint("AK_VehicleClasses_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_VehicleClasses_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{1,39}$'");
                    table.ForeignKey(
                        name: "FK_VehicleClasses_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleClassRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleClassRates", x => x.Id);
                    table.CheckConstraint("CK_VehicleClassRates_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_VehicleClassRates_VehicleClasses_MunicipalityId_VehicleClas~",
                        columns: x => new { x.MunicipalityId, x.VehicleClassId },
                        principalTable: "VehicleClasses",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings",
                sql: "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)) OR (\"Basis\" = 3 AND \"FixedAmount\" IS NULL AND \"MaximumAmount\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings",
                sql: "\"Basis\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleClasses_MunicipalityId_Code",
                table: "VehicleClasses",
                columns: new[] { "MunicipalityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleClassRates_MunicipalityId_VehicleClassId_EffectiveDa~",
                table: "VehicleClassRates",
                columns: new[] { "MunicipalityId", "VehicleClassId", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleClassRates");

            migrationBuilder.DropTable(
                name: "VehicleClasses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_AmountShape",
                table: "GovernedServiceSettings",
                sql: "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GovernedServiceSettings_Basis",
                table: "GovernedServiceSettings",
                sql: "\"Basis\" IN (1, 2)");
        }
    }
}
