using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaughterAnimalLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL's ordinary text unique index is case-sensitive. Keep the existing exact-name index and add
            // this tenant-scoped normalized guard so direct or concurrent writes cannot create Goat/goat/GOAT.
            // Unfiltered by design: the existing index does not permit silently reusing a soft-deleted name either.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "UX_SlaughterAnimalRates_Municipality_NormalizedName"
                ON "SlaughterAnimalRates" ("MunicipalityId", lower(btrim("AnimalName")));
                """);

            migrationBuilder.CreateTable(
                name: "SlaughterAnimalLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimalType = table.Column<int>(type: "integer", nullable: false),
                    DisplayLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaughterAnimalLabels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlaughterAnimalLabels_MunicipalityId_AnimalType",
                table: "SlaughterAnimalLabels",
                columns: new[] { "MunicipalityId", "AnimalType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaughterAnimalLabels");

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_SlaughterAnimalRates_Municipality_NormalizedName";
                """);
        }
    }
}
