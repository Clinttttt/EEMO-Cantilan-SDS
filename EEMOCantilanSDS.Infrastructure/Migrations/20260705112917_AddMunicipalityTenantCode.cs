using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalityTenantCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantCode",
                table: "Municipalities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Additive + safe on the live DB: backfill existing rows to their stable, distinct cache
            // namespace BEFORE the unique index is created. Cantilan keeps "cantilan-sds" (== today's
            // DefaultTenantCode) so its claim/cache are byte-for-byte unchanged. Any row not matched by a
            // known code falls back to LOWER("Code") so none is left empty (which would break the unique index).
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = 'cantilan-sds' WHERE ""Code"" = 'CANTILAN';");
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = 'carrascal' WHERE ""Code"" = 'CARRASCAL';");
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = 'madrid' WHERE ""Code"" = 'MADRID';");
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = 'carmen' WHERE ""Code"" = 'CARMEN';");
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = 'lanuza' WHERE ""Code"" = 'LANUZA';");
            migrationBuilder.Sql(@"UPDATE ""Municipalities"" SET ""TenantCode"" = LOWER(""Code"") WHERE ""TenantCode"" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Municipalities_TenantCode",
                table: "Municipalities",
                column: "TenantCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Municipalities_TenantCode",
                table: "Municipalities");

            migrationBuilder.DropColumn(
                name: "TenantCode",
                table: "Municipalities");
        }
    }
}
