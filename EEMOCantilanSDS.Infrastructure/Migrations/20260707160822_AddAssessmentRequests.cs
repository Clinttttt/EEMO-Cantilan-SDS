using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Province = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RequestingOffice = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    FocalPerson = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Position = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OfficialEmail = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FacilitiesManaged = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApproxVendors = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Acknowledged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DecisionMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OnboardingLink = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_AssessmentRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRequests_Status",
                table: "AssessmentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRequests_SubmittedAt",
                table: "AssessmentRequests",
                column: "SubmittedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentRequests");
        }
    }
}
