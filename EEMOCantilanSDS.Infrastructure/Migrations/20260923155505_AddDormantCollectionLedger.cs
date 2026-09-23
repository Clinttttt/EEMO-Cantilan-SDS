using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDormantCollectionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "Id" });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.UniqueConstraint("AK_Collections_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                    table.CheckConstraint("CK_Collections_TotalAmount_Positive", "\"TotalAmount\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "CollectionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueClassificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueClassificationPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePart = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionLines", x => x.Id);
                    table.CheckConstraint("CK_CollectionLines_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CollectionLines_SourceShape", "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_CollectionLines_Collections_MunicipalityId_CollectionId",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionLines_RevenueClassificationPolicies_MunicipalityI~",
                        columns: x => new { x.MunicipalityId, x.RevenueClassificationId, x.RevenueClassificationPolicyId },
                        principalTable: "RevenueClassificationPolicies",
                        principalColumns: new[] { "MunicipalityId", "RevenueClassificationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionLines_RevenueClassifications_MunicipalityId_Reven~",
                        columns: x => new { x.MunicipalityId, x.RevenueClassificationId },
                        principalTable: "RevenueClassifications",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLines_MunicipalityId_CollectionId",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLines_MunicipalityId_RevenueClassificationId_Reve~",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "RevenueClassificationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLines_MunicipalityId_SourceKind_SourceId_SourcePa~",
                table: "CollectionLines",
                columns: new[] { "MunicipalityId", "SourceKind", "SourceId", "SourcePart" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_ClientOperationId",
                table: "Collections",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MunicipalityId_BusinessDate",
                table: "Collections",
                columns: new[] { "MunicipalityId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_MunicipalityId_CollectorId_BusinessDate",
                table: "Collections",
                columns: new[] { "MunicipalityId", "CollectorId", "BusinessDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionLines");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies");
        }
    }
}
