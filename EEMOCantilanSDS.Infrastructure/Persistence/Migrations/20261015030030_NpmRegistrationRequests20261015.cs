using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NpmRegistrationRequests20261015 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_FishMeatNpmAssociations_MunicipalityId_Id",
                table: "FishMeatNpmAssociations",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateTable(
                name: "NpmRegistrationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VendorRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxYear = table.Column<int>(type: "integer", nullable: false),
                    VendorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReplacesRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedStall = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProposedSection = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DecisionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssociationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpmRegistrationRequests", x => x.Id);
                    table.UniqueConstraint("AK_NpmRegistrationRequests_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_NpmRegistrationRequest_State", "\"Status\" IN (1,2,3,4) AND \"Version\" > 0 AND ((\"Status\" = 1 AND \"DecisionOperationId\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"AssociationId\" IS NULL) OR (\"Status\" <> 1 AND \"DecisionOperationId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedBy\" IS NOT NULL AND \"ReviewReason\" IS NOT NULL)) AND ((\"Status\" = 2 AND \"AssociationId\" IS NOT NULL) OR (\"Status\" <> 2 AND \"AssociationId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_NpmRegistrationRequests_FishMeatNpmAssociations_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.AssociationId },
                        principalTable: "FishMeatNpmAssociations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NpmRegistrationRequests_FishMeatVendorRegistrations_Municip~",
                        columns: x => new { x.MunicipalityId, x.VendorRegistrationId },
                        principalTable: "FishMeatVendorRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NpmRegistrationRequests_NpmRegistrationRequests_Municipalit~",
                        columns: x => new { x.MunicipalityId, x.ReplacesRequestId },
                        principalTable: "NpmRegistrationRequests",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NpmRegistrationRequests_Users_MunicipalityId_CollectorId",
                        columns: x => new { x.MunicipalityId, x.CollectorId },
                        principalTable: "Users",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_AssociationId",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "AssociationId" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_ClientOperationId",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_CollectorId_VendorRe~",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "CollectorId", "VendorRegistrationId" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_DecisionOperationId",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "DecisionOperationId" },
                unique: true,
                filter: "\"DecisionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_ReplacesRequestId",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "ReplacesRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_SubmittedAtUtc_Id",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "SubmittedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NpmRegistrationRequests_MunicipalityId_VendorRegistrationId",
                table: "NpmRegistrationRequests",
                columns: new[] { "MunicipalityId", "VendorRegistrationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NpmRegistrationRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FishMeatNpmAssociations_MunicipalityId_Id",
                table: "FishMeatNpmAssociations");
        }
    }
}
