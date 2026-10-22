using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <summary>
    /// Two nullable columns on the approved fee options: the animals a Slaughterhouse charge applies to and whether its quantity is
    /// the head count or a unit count. Additive only: every existing fee option keeps null (not animal-specific) and nothing is rewritten.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019090000_AddSlaughterChargeScope20261019")]
    public partial class AddSlaughterChargeScope20261019 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "ApplicableAnimals", table: "GovernedServiceFeeOptions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "QuantityBasis", table: "GovernedServiceFeeOptions", type: "integer", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApplicableAnimals", table: "GovernedServiceFeeOptions");
            migrationBuilder.DropColumn(name: "QuantityBasis", table: "GovernedServiceFeeOptions");
        }
    }
}
