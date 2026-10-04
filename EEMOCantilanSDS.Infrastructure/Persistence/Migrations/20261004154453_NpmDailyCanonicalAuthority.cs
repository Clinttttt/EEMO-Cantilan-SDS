using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NpmDailyCanonicalAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CanonicalCollectionId",
                table: "DailyCollections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SettlementAuthorityState",
                table: "DailyCollections",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanonicalCollectionId",
                table: "DailyCollections");

            migrationBuilder.DropColumn(
                name: "SettlementAuthorityState",
                table: "DailyCollections");
        }
    }
}
