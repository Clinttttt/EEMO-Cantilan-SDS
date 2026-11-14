using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentOfficeDecision20261104 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CapturedAmount",
                table: "OnlinePaymentTransactions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispositionAtUtc",
                table: "OnlinePaymentTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispositionBy",
                table: "OnlinePaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispositionNote",
                table: "OnlinePaymentTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispositionReference",
                table: "OnlinePaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationReason",
                table: "OnlinePaymentTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapturedAmount",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DispositionAtUtc",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DispositionBy",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DispositionNote",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DispositionReference",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationReason",
                table: "OnlinePaymentTransactions");
        }
    }
}
