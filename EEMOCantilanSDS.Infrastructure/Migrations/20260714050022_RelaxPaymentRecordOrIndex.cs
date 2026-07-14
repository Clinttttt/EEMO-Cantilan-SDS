using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelaxPaymentRecordOrIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "ORNumber" },
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_MunicipalityId_ORNumber",
                table: "PaymentRecords",
                columns: new[] { "MunicipalityId", "ORNumber" },
                unique: true,
                filter: "\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");
        }
    }
}
