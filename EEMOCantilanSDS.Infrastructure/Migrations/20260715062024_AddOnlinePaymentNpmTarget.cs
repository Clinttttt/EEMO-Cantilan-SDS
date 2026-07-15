using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePaymentNpmTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentRecordId",
                table: "OnlinePaymentTransactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TargetMonth",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetStallId",
                table: "OnlinePaymentTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetYear",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetKind",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TargetMonth",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TargetStallId",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TargetYear",
                table: "OnlinePaymentTransactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentRecordId",
                table: "OnlinePaymentTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
