using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenPayorHistoricalAccessTenantKeys20261026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayorLiabilityLinks_Users_PayorUserId",
                table: "PayorLiabilityLinks");

            migrationBuilder.CreateIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_ContractId",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_ObligationAccountId",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "ObligationAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_PayorUserId",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "PayorUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayorHistoricalAccessCodes_Contracts_MunicipalityId_Contrac~",
                table: "PayorHistoricalAccessCodes",
                columns: new[] { "MunicipalityId", "ContractId" },
                principalTable: "Contracts",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorHistoricalAccessCodes_Municipalities_MunicipalityId",
                table: "PayorHistoricalAccessCodes",
                column: "MunicipalityId",
                principalTable: "Municipalities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorHistoricalAccessCodes_ObligationAccounts_MunicipalityI~",
                table: "PayorHistoricalAccessCodes",
                columns: new[] { "MunicipalityId", "ObligationAccountId" },
                principalTable: "ObligationAccounts",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorLiabilityLinks_Contracts_MunicipalityId_ContractId",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "ContractId" },
                principalTable: "Contracts",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorLiabilityLinks_Municipalities_MunicipalityId",
                table: "PayorLiabilityLinks",
                column: "MunicipalityId",
                principalTable: "Municipalities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorLiabilityLinks_ObligationAccounts_MunicipalityId_Oblig~",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "ObligationAccountId" },
                principalTable: "ObligationAccounts",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayorLiabilityLinks_Users_MunicipalityId_PayorUserId",
                table: "PayorLiabilityLinks",
                columns: new[] { "MunicipalityId", "PayorUserId" },
                principalTable: "Users",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayorHistoricalAccessCodes_Contracts_MunicipalityId_Contrac~",
                table: "PayorHistoricalAccessCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorHistoricalAccessCodes_Municipalities_MunicipalityId",
                table: "PayorHistoricalAccessCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorHistoricalAccessCodes_ObligationAccounts_MunicipalityI~",
                table: "PayorHistoricalAccessCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorLiabilityLinks_Contracts_MunicipalityId_ContractId",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorLiabilityLinks_Municipalities_MunicipalityId",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorLiabilityLinks_ObligationAccounts_MunicipalityId_Oblig~",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_PayorLiabilityLinks_Users_MunicipalityId_PayorUserId",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_ContractId",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_ObligationAccountId",
                table: "PayorLiabilityLinks");

            migrationBuilder.DropIndex(
                name: "IX_PayorLiabilityLinks_MunicipalityId_PayorUserId",
                table: "PayorLiabilityLinks");

            migrationBuilder.AddForeignKey(
                name: "FK_PayorLiabilityLinks_Users_PayorUserId",
                table: "PayorLiabilityLinks",
                column: "PayorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
