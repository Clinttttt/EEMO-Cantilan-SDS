using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentMonthsAndLateCharges20261125 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LateCharges",
                table: "OnlinePaymentTransactions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Months",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateCharges",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "Months",
                table: "OnlinePaymentTransactions");
        }
    }
}
