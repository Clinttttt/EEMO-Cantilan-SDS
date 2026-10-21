using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpaceHolderImportRunOperationId20261013 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "SpaceHolderImportRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpaceHolderImportRuns_MunicipalityId_ClientOperationId",
                table: "SpaceHolderImportRuns",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpaceHolderImportRuns_MunicipalityId_ClientOperationId",
                table: "SpaceHolderImportRuns");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "SpaceHolderImportRuns");
        }
    }
}
