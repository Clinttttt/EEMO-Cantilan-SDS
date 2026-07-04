using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityBillingArchetype : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Archetype",
                table: "Facilities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Map existing facilities to their billing archetype by code (Code: NPM=1 TCC=2 NCC=3 BBQ=4
            // ICE=5 SLH=6 TRM=7 TPM=8; Archetype: DailyStall=1 MonthlyRental=2 WeeklyMarket=3 PerTrip=4
            // PerHead=5 Custom=99). Behaviour-neutral: nothing reads Archetype yet.
            migrationBuilder.Sql(@"
UPDATE ""Facilities"" SET ""Archetype"" = CASE ""Code""
    WHEN 1 THEN 1   -- NPM  -> DailyStall
    WHEN 2 THEN 2   -- TCC  -> MonthlyRental
    WHEN 3 THEN 2   -- NCC  -> MonthlyRental
    WHEN 4 THEN 2   -- BBQ  -> MonthlyRental
    WHEN 5 THEN 2   -- ICE  -> MonthlyRental
    WHEN 6 THEN 5   -- SLH  -> PerHead
    WHEN 7 THEN 4   -- TRM  -> PerTrip
    WHEN 8 THEN 3   -- TPM  -> WeeklyMarket
    ELSE 99         -- Custom
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Archetype",
                table: "Facilities");
        }
    }
}
