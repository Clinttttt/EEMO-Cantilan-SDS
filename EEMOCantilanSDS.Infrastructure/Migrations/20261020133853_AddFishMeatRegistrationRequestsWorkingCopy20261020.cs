using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFishMeatRegistrationRequestsWorkingCopy20261020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FishMeatRegistrationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PendingKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VendorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VendorType = table.Column<int>(type: "integer", nullable: false),
                    TaxYear = table.Column<int>(type: "integer", nullable: false),
                    Contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReplacesRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DecisionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FishMeatRegistrationRequests", x => x.Id);
                    table.UniqueConstraint("AK_FishMeatRegistrationRequests_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_FishMeatRequest_Identity", "\"TaxYear\" BETWEEN 2000 AND 2200 AND \"VendorType\" IN (1,2) AND length(btrim(\"VendorName\")) > 0");
                    table.CheckConstraint("CK_FishMeatRequest_ReviewEvidence", "(\"Status\" = 1 AND \"ReviewedByUserId\" IS NULL) OR (\"Status\" <> 1 AND \"ReviewedByUserId\" IS NOT NULL AND \"Reason\" IS NOT NULL)");
                    table.CheckConstraint("CK_FishMeatRequest_State", "\"Status\" IN (1,2,3,4,5) AND ((\"Status\" = 1 AND \"Version\" = 1 AND \"DecisionOperationId\" IS NULL AND \"DecisionFingerprint\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedBy\" IS NULL AND \"Reason\" IS NULL) OR (\"Status\" <> 1 AND \"Version\" = 2 AND \"DecisionOperationId\" IS NOT NULL AND \"DecisionFingerprint\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedBy\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)) AND ((\"Status\" = 2 AND \"RegistrationId\" IS NOT NULL) OR (\"Status\" <> 2 AND \"RegistrationId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_FishMeatRegistrationRequests_FishMeatRegistrationRequests_M~",
                        columns: x => new { x.MunicipalityId, x.ReplacesRequestId },
                        principalTable: "FishMeatRegistrationRequests",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FishMeatRegistrationRequests_FishMeatVendorRegistrations_Mu~",
                        columns: x => new { x.MunicipalityId, x.RegistrationId },
                        principalTable: "FishMeatVendorRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FishMeatRegistrationRequests_Users_MunicipalityId_Collector~",
                        columns: x => new { x.MunicipalityId, x.CollectorId },
                        principalTable: "Users",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FishMeatRegistrationRequests_Users_MunicipalityId_ReviewedB~",
                        columns: x => new { x.MunicipalityId, x.ReviewedByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_ClientOperation~",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_CollectorId_Pen~",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "CollectorId", "PendingKey" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_DecisionOperati~",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "DecisionOperationId" },
                unique: true,
                filter: "\"DecisionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_RegistrationId",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "RegistrationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_ReplacesRequest~",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "ReplacesRequestId" },
                unique: true,
                filter: "\"ReplacesRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_ReviewedByUserId",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "ReviewedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatRegistrationRequests_MunicipalityId_Status_Submitte~",
                table: "FishMeatRegistrationRequests",
                columns: new[] { "MunicipalityId", "Status", "SubmittedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FishMeatRegistrationRequests");
        }
    }
}
