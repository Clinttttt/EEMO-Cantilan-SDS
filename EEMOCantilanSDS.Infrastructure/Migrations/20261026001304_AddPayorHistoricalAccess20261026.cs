using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayorHistoricalAccess20261026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayorHistoricalAccessCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TermStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayorUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_PayorHistoricalAccessCodes", x => x.Id);
                    table.CheckConstraint("CK_PayorHistoricalAccessCodes_OneIdentity", "(\"ContractId\" IS NOT NULL) <> (\"ObligationAccountId\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "PayorLiabilityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObligationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TermStart = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayorLiabilityLinks", x => x.Id);
                    table.CheckConstraint("CK_PayorLiabilityLinks_OneIdentity", "(\"ContractId\" IS NOT NULL) <> (\"ObligationAccountId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PayorLiabilityLinks_Users_PayorUserId",
                        column: x => x.PayorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayorHistoricalAccessCodes_Code",
                table: "PayorHistoricalAccessCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayorHistoricalAccessCodes_MunicipalityId_ContractId",
                table: "PayorHistoricalAccessCodes",
                columns: new[] { "MunicipalityId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayorHistoricalAccessCodes_MunicipalityId_ObligationAccount~",
                table: "PayorHistoricalAccessCodes",
                columns: new[] { "MunicipalityId", "ObligationAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayorLiabilityLinks_PayorUserId_ContractId",
                table: "PayorLiabilityLinks",
                columns: new[] { "PayorUserId", "ContractId" },
                unique: true,
                filter: "\"ContractId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayorLiabilityLinks_PayorUserId_ObligationAccountId",
                table: "PayorLiabilityLinks",
                columns: new[] { "PayorUserId", "ObligationAccountId" },
                unique: true,
                filter: "\"ObligationAccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayorHistoricalAccessCodes");

            migrationBuilder.DropTable(
                name: "PayorLiabilityLinks");
        }
    }
}
