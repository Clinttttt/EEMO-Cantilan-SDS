using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilitySectionLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FishSectionLabel",
                table: "Facilities",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeatSectionLabel",
                table: "Facilities",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VegetableSectionLabel",
                table: "Facilities",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FishSectionLabel",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "MeatSectionLabel",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "VegetableSectionLabel",
                table: "Facilities");
        }
    }
}
