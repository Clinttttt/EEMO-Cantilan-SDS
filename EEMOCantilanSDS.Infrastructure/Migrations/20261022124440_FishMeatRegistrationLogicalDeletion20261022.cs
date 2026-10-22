using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FishMeatRegistrationLogicalDeletion20261022 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.AddColumn<Guid>(
                name: "DeleteClientOperationId",
                table: "FishMeatVendorRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeleteReason",
                table: "FishMeatVendorRegistrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "FishMeatVendorRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "FishMeatVendorRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_DeleteClientOper~",
                table: "FishMeatVendorRegistrations",
                columns: new[] { "MunicipalityId", "DeleteClientOperationId" },
                unique: true,
                filter: "\"DeleteClientOperationId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VendorRegistration_Deletion",
                table: "FishMeatVendorRegistrations",
                sql: "(\"Status\" IN (1,2) AND \"DeleteClientOperationId\" IS NULL AND \"DeletedAtUtc\" IS NULL AND \"DeletedBy\" IS NULL AND \"DeleteReason\" IS NULL) OR (\"Status\" = 3 AND \"DeleteClientOperationId\" IS NOT NULL AND \"DeletedAtUtc\" IS NOT NULL AND \"DeletedBy\" IS NOT NULL AND \"DeleteReason\" IS NOT NULL AND length(btrim(\"DeletedBy\")) > 0 AND length(btrim(\"DeleteReason\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations",
                sql: "(\"Status\" = 1 AND \"CloseClientOperationId\" IS NULL AND \"ClosedOn\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"ClosedBy\" IS NULL AND \"CloseNote\" IS NULL) OR (\"Status\" IN (2,3) AND \"CloseClientOperationId\" IS NOT NULL AND \"ClosedOn\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedBy\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tombstone evidence is irreversible through schema rollback; never discard it.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "FishMeatVendorRegistrations" WHERE "Status" = 3) THEN
                    RAISE EXCEPTION 'Cannot roll back vendor deletion schema while tombstones exist';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_FishMeatVendorRegistrations_MunicipalityId_DeleteClientOper~",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VendorRegistration_Deletion",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "DeleteClientOperationId",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "DeleteReason",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VendorRegistration_Lifecycle",
                table: "FishMeatVendorRegistrations",
                sql: "(\"Status\" = 1 AND \"CloseClientOperationId\" IS NULL AND \"ClosedOn\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"ClosedBy\" IS NULL AND \"CloseNote\" IS NULL) OR (\"Status\" = 2 AND \"CloseClientOperationId\" IS NOT NULL AND \"ClosedOn\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedBy\" IS NOT NULL)");
        }
    }
}
