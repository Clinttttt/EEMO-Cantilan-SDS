using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNpmFishDayOnlineTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeclaredFishKilos",
                table: "OnlinePaymentTransactions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDay",
                table: "OnlinePaymentTransactions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredFishKilos",
                table: "OnlinePaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TargetDay",
                table: "OnlinePaymentTransactions");
        }
    }
}
