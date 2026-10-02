using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemittanceSubmissionGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "CollectionRemittances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionSequence",
                table: "CollectionRemittances",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittances_MunicipalityId_SubmissionId_Collector~",
                table: "CollectionRemittances",
                columns: new[] { "MunicipalityId", "SubmissionId", "CollectorId" },
                unique: true,
                filter: "\"SubmissionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRemittances_MunicipalityId_SubmissionId_Submissio~",
                table: "CollectionRemittances",
                columns: new[] { "MunicipalityId", "SubmissionId", "SubmissionSequence" },
                unique: true,
                filter: "\"SubmissionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CollectionRemittances_MunicipalityId_SubmissionId_Collector~",
                table: "CollectionRemittances");

            migrationBuilder.DropIndex(
                name: "IX_CollectionRemittances_MunicipalityId_SubmissionId_Submissio~",
                table: "CollectionRemittances");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "CollectionRemittances");

            migrationBuilder.DropColumn(
                name: "SubmissionSequence",
                table: "CollectionRemittances");
        }
    }
}
