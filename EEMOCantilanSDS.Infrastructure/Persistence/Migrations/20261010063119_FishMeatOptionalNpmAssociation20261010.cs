using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FishMeatOptionalNpmAssociation20261010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Contracts_MunicipalityId_Id_StallId",
                table: "Contracts",
                columns: new[] { "MunicipalityId", "Id", "StallId" });

            migrationBuilder.CreateTable(
                name: "FishMeatNpmAssociations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    StallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccupantSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmationNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConfirmedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrectionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrectedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FishMeatNpmAssociations", x => x.Id);
                    table.CheckConstraint("CK_NpmAssociation_Correction", "(\"CorrectedAtUtc\" IS NULL AND \"CorrectionOperationId\" IS NULL AND \"CorrectedBy\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"CorrectedAtUtc\" IS NOT NULL AND \"CorrectionOperationId\" IS NOT NULL AND \"CorrectedBy\" IS NOT NULL AND \"CorrectionReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FishMeatNpmAssociations_Contracts_MunicipalityId_ContractId~",
                        columns: x => new { x.MunicipalityId, x.ContractId, x.StallId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "MunicipalityId", "Id", "StallId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FishMeatNpmAssociations_FishMeatVendorRegistrations_Municip~",
                        columns: x => new { x.MunicipalityId, x.VendorRegistrationId },
                        principalTable: "FishMeatVendorRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatNpmAssociations_MunicipalityId_ClientOperationId",
                table: "FishMeatNpmAssociations",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatNpmAssociations_MunicipalityId_ContractId_StallId",
                table: "FishMeatNpmAssociations",
                columns: new[] { "MunicipalityId", "ContractId", "StallId" });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatNpmAssociations_MunicipalityId_CorrectionOperationId",
                table: "FishMeatNpmAssociations",
                columns: new[] { "MunicipalityId", "CorrectionOperationId" },
                unique: true,
                filter: "\"CorrectionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatNpmAssociations_MunicipalityId_VendorRegistrationId~",
                table: "FishMeatNpmAssociations",
                columns: new[] { "MunicipalityId", "VendorRegistrationId", "ContractId" },
                unique: true,
                filter: "\"CorrectedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FishMeatNpmAssociations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Contracts_MunicipalityId_Id_StallId",
                table: "Contracts");
        }
    }
}
