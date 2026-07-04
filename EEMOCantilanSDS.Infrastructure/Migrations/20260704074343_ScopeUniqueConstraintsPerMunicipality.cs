using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopeUniqueConstraintsPerMunicipality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_ElecORNumber",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_WaterORNumber",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TrmTrips_ORNumber",
                table: "TrmTrips");

            migrationBuilder.DropIndex(
                name: "IX_TpmAttendances_ORNumber",
                table: "TpmAttendances");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_ORNumber",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_NpmMarketClosures_ClosureDate",
                table: "NpmMarketClosures");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_Code",
                table: "Facilities");

            migrationBuilder.DropIndex(
                name: "IX_DailyCollections_ORNumber",
                table: "DailyCollections");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_MunicipalityId_ElecORNumber",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "ElecORNumber" },
                unique: true,
                filter: "\"ElecORNumber\" IS NOT NULL AND \"ElecORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_MunicipalityId_WaterORNumber",
                table: "UtilityBills",
                columns: new[] { "MunicipalityId", "WaterORNumber" },
                unique: true,
                filter: "\"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Users_MunicipalityId_Email",
                table: "Users",
                columns: new[] { "MunicipalityId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_MunicipalityId_Username",
                table: "Users",
                columns: new[] { "MunicipalityId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrmTrips_MunicipalityId_ORNumber",
                table: "TrmTrips",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TpmAttendances_MunicipalityId_ORNumber",
                table: "TpmAttendances",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_NpmMarketClosures_MunicipalityId_ClosureDate",
                table: "NpmMarketClosures",
                columns: new[] { "MunicipalityId", "ClosureDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_MunicipalityId_Code",
                table: "Facilities",
                columns: new[] { "MunicipalityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_MunicipalityId_ElecORNumber",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_UtilityBills_MunicipalityId_WaterORNumber",
                table: "UtilityBills");

            migrationBuilder.DropIndex(
                name: "IX_Users_MunicipalityId_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_MunicipalityId_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TrmTrips_MunicipalityId_ORNumber",
                table: "TrmTrips");

            migrationBuilder.DropIndex(
                name: "IX_TpmAttendances_MunicipalityId_ORNumber",
                table: "TpmAttendances");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_NpmMarketClosures_MunicipalityId_ClosureDate",
                table: "NpmMarketClosures");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_MunicipalityId_Code",
                table: "Facilities");

            migrationBuilder.DropIndex(
                name: "IX_DailyCollections_MunicipalityId_ORNumber",
                table: "DailyCollections");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ElecORNumber",
                table: "UtilityBills",
                column: "ElecORNumber",
                unique: true,
                filter: "\"ElecORNumber\" IS NOT NULL AND \"ElecORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_WaterORNumber",
                table: "UtilityBills",
                column: "WaterORNumber",
                unique: true,
                filter: "\"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrmTrips_ORNumber",
                table: "TrmTrips",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TpmAttendances_ORNumber",
                table: "TpmAttendances",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_ORNumber",
                table: "PaymentRecords",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_NpmMarketClosures_ClosureDate",
                table: "NpmMarketClosures",
                column: "ClosureDate",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_Code",
                table: "Facilities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyCollections_ORNumber",
                table: "DailyCollections",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }
    }
}
