using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevenueClassificationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevenueClassifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueClassifications", x => x.Id);
                    table.UniqueConstraint("AK_RevenueClassifications_MunicipalityId_Id", x => new { x.MunicipalityId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "RevenueClassificationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueClassificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PermittedInstrumentType = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueClassificationPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevenueClassificationPolicies_RevenueClassifications_Munici~",
                        columns: x => new { x.MunicipalityId, x.RevenueClassificationId },
                        principalTable: "RevenueClassifications",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~",
                table: "RevenueClassificationPolicies",
                columns: new[] { "MunicipalityId", "RevenueClassificationId", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueClassifications_MunicipalityId_SemanticCode",
                table: "RevenueClassifications",
                columns: new[] { "MunicipalityId", "SemanticCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevenueClassificationPolicies");

            migrationBuilder.DropTable(
                name: "RevenueClassifications");
        }
    }
}
