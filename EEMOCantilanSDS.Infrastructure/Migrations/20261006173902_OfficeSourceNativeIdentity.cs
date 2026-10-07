using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfficeSourceNativeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TerminalSection",
                table: "VehicleClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FishMeatVendorRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxYear = table.Column<int>(type: "integer", nullable: false),
                    VendorType = table.Column<int>(type: "integer", nullable: false),
                    RegistrationKind = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FishMeatVendorRegistrations", x => x.Id);
                    table.CheckConstraint("CK_VendorRegistration_Type", "\"VendorType\" IN (1,2) AND \"RegistrationKind\" IN (1,2)");
                    table.CheckConstraint("CK_VendorRegistration_Year", "\"TaxYear\" BETWEEN 2000 AND 2200");
                    table.ForeignKey(
                        name: "FK_FishMeatVendorRegistrations_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_ClientOperationId",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "TerminalSection",
                table: "VehicleClasses");
        }
    }
}
