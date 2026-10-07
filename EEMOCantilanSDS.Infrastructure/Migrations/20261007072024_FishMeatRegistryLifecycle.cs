using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FishMeatRegistryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CloseClientOperationId",
                table: "FishMeatVendorRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseNote",
                table: "FishMeatVendorRegistrations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "FishMeatVendorRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "FishMeatVendorRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClosedOn",
                table: "FishMeatVendorRegistrations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriorRegistrationId",
                table: "FishMeatVendorRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "FishMeatVendorRegistrations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FishMeatVendorRegistrations_MunicipalityId_Id",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_CloseClientOpera~",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "CloseClientOperationId" },
                unique: true,
                filter: "\"CloseClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_PriorRegistratio~",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "PriorRegistrationId", "TaxYear" },
                unique: true,
                filter: "\"PriorRegistrationId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations",
                sql: "(\"Status\" = 1 AND \"CloseClientOperationId\" IS NULL AND \"ClosedOn\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"ClosedBy\" IS NULL AND \"CloseNote\" IS NULL) OR (\"Status\" = 2 AND \"CloseClientOperationId\" IS NOT NULL AND \"ClosedOn\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedBy\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_FishMeatVendorRegistrations_FishMeatVendorRegistrations_Mun~",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "PriorRegistrationId" },
                principalTable: "FishMeatVendorRegistrations",
                principalColumns: new[] { "MunicipalityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FishMeatVendorRegistrations_FishMeatVendorRegistrations_Mun~",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FishMeatVendorRegistrations_MunicipalityId_Id",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_CloseClientOpera~",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_PriorRegistratio~",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "CloseClientOperationId",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "CloseNote",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "ClosedOn",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "PriorRegistrationId",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "FishMeatVendorRegistrations");
        }
    }
}
