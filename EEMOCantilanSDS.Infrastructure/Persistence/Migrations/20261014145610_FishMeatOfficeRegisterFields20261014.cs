using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FishMeatOfficeRegisterFields20261014 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "FishMeatVendorRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "FishMeatVendorRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "FishMeatVendorRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameExtension",
                table: "FishMeatVendorRegistrations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NatureOfBusiness",
                table: "FishMeatVendorRegistrations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sex",
                table: "FishMeatVendorRegistrations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "NameExtension",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "NatureOfBusiness",
                table: "FishMeatVendorRegistrations");

            migrationBuilder.DropColumn(
                name: "Sex",
                table: "FishMeatVendorRegistrations");
        }
    }
}
