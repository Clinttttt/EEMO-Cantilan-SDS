using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KanmanggaySpaceLeaseEvidence20261009 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreaLocation",
                table: "ObligationAccounts",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AreaSqm",
                table: "ObligationAccounts",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ContractEffectiveDate",
                table: "ObligationAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractName",
                table: "ObligationAccounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractYears",
                table: "ObligationAccounts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaLocation",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "AreaSqm",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "ContractEffectiveDate",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "ContractName",
                table: "ObligationAccounts");

            migrationBuilder.DropColumn(
                name: "ContractYears",
                table: "ObligationAccounts");
        }
    }
}
