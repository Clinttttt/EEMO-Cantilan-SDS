using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalityIdToHiddenSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiddenSuggestions_Type_Value",
                table: "HiddenSuggestions");

            migrationBuilder.AddColumn<Guid>(
                name: "MunicipalityId",
                table: "HiddenSuggestions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing (pre-multi-tenant) blocklist rows to the default municipality (Cantilan) so
            // they stay visible under its tenant filter. Guarded so it is a no-op if no default exists yet
            // (leaves rows unstamped rather than violating NOT NULL). Safe + idempotent.
            migrationBuilder.Sql(@"
                UPDATE ""HiddenSuggestions""
                SET ""MunicipalityId"" = (SELECT ""Id"" FROM ""Municipalities"" WHERE ""IsDefault"" = TRUE LIMIT 1)
                WHERE ""MunicipalityId"" = '00000000-0000-0000-0000-000000000000'
                  AND EXISTS (SELECT 1 FROM ""Municipalities"" WHERE ""IsDefault"" = TRUE);");

            migrationBuilder.CreateIndex(
                name: "IX_HiddenSuggestions_MunicipalityId_Type_Value",
                table: "HiddenSuggestions",
                columns: new[] { "MunicipalityId", "Type", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiddenSuggestions_MunicipalityId_Type_Value",
                table: "HiddenSuggestions");

            migrationBuilder.DropColumn(
                name: "MunicipalityId",
                table: "HiddenSuggestions");

            migrationBuilder.CreateIndex(
                name: "IX_HiddenSuggestions_Type_Value",
                table: "HiddenSuggestions",
                columns: new[] { "Type", "Value" },
                unique: true);
        }
    }
}
