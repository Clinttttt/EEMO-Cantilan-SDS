using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientOperationIdForOfflineSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "TrmTrips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "TpmAttendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "SlaughterTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "PaymentRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "DailyCollections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrmTrips_ClientOperationId",
                table: "TrmTrips",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TpmAttendances_ClientOperationId",
                table: "TpmAttendances",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlaughterTransactions_ClientOperationId",
                table: "SlaughterTransactions",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_ClientOperationId",
                table: "PaymentRecords",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCollections_ClientOperationId",
                table: "DailyCollections",
                column: "ClientOperationId",
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrmTrips_ClientOperationId",
                table: "TrmTrips");

            migrationBuilder.DropIndex(
                name: "IX_TpmAttendances_ClientOperationId",
                table: "TpmAttendances");

            migrationBuilder.DropIndex(
                name: "IX_SlaughterTransactions_ClientOperationId",
                table: "SlaughterTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_ClientOperationId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_DailyCollections_ClientOperationId",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "TrmTrips");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "TpmAttendances");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "SlaughterTransactions");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "DailyCollections");
        }
    }
}
