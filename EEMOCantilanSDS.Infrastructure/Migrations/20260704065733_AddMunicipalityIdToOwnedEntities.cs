using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalityIdToOwnedEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "UtilityBills",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "TrmTrips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "TrmTransporters",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "TpmVendors",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "TpmAttendances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "Stalls",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "StallMonthlyExceptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "SlaughterTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "PayorStallLinks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "PayorActivationCodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "PaymentRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "OnlinePaymentTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "NpmMarketClosures",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "Facilities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "DailyCollections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "Contracts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "CollectorFacilityAssignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill every existing (Cantilan) row to the default municipality. Done in one atomic
            // block that resolves the default municipality id via IsDefault; if none exists yet, it does
            // nothing (rows keep the empty default) so the migration can never fail on a fresh database.
            migrationBuilder.Sql(@"
DO $$
DECLARE default_mun uuid;
BEGIN
    SELECT ""Id"" INTO default_mun FROM ""Municipalities"" WHERE ""IsDefault"" = true LIMIT 1;
    IF default_mun IS NOT NULL THEN
        UPDATE ""Facilities""                  SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""Stalls""                      SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""Contracts""                   SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""PaymentRecords""              SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""DailyCollections""            SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""UtilityBills""                SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""StallMonthlyExceptions""      SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""NpmMarketClosures""           SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""OnlinePaymentTransactions""   SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""SlaughterTransactions""       SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""TpmVendors""                  SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""TpmAttendances""              SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""TrmTransporters""             SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""TrmTrips""                    SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""Users""                       SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""CollectorFacilityAssignments"" SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""PayorActivationCodes""        SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""PayorStallLinks""             SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
        UPDATE ""AuditLogs""                   SET ""MunicipalityId"" = default_mun WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000';
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "UtilityBills");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "TrmTrips");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "TrmTransporters");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "TpmVendors");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "TpmAttendances");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "Stalls");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "StallMonthlyExceptions");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "SlaughterTransactions");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "PayorStallLinks");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "PayorActivationCodes");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "NpmMarketClosures");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "CollectorFacilityAssignments");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "AuditLogs");
        }
    }
}
