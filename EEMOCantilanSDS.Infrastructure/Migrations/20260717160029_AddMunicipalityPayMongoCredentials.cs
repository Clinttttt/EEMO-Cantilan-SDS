using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalityPayMongoCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayMongoPublicKey",
                table: "Municipalities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoSecretKeyEnc",
                table: "Municipalities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoWebhookSecretEnc",
                table: "Municipalities",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayMongoPublicKey",
                table: "Municipalities");

            migrationBuilder.DropColumn(
                name: "PayMongoSecretKeyEnc",
                table: "Municipalities");

            migrationBuilder.DropColumn(
                name: "PayMongoWebhookSecretEnc",
                table: "Municipalities");
        }
    }
}
