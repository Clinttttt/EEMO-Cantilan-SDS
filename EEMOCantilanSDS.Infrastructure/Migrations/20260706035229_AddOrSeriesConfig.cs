using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrSeriesConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrSeriesConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    PadWidth = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_OrSeriesConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrSeriesConfigs_MunicipalityId",
                table: "OrSeriesConfigs",
                column: "MunicipalityId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrSeriesConfigs");
        }
    }
}
