using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentWeightMeasureRateVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeighingRateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorType = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RatePerKilo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighingRateVersions", x => x.Id);
                    table.CheckConstraint("CK_WeighingRateVersions_Amount", "(\"IsEnabled\" = TRUE AND \"RatePerKilo\" > 0) OR (\"IsEnabled\" = FALSE AND \"RatePerKilo\" IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeighingRateVersions_MunicipalityId_VendorType_EffectiveDat~",
                table: "WeighingRateVersions",
                columns: new[] { "MunicipalityId", "VendorType", "EffectiveDate", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeighingRateVersions");
        }
    }
}
