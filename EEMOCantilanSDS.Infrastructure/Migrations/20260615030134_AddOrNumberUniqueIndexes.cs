using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrNumberUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_DailyCollections_ORNumber",
                table: "DailyCollections",
                column: "ORNumber",
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_DailyCollections_ORNumber",
                table: "DailyCollections");
        }
    }
}
