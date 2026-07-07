using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Province = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsSubmittedForValidation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingDrafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingDrafts_AssessmentRequestId",
                table: "OnboardingDrafts",
                column: "AssessmentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingDrafts_Token",
                table: "OnboardingDrafts",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingDrafts");
        }
    }
}
