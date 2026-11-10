using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentInstalments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentInstalments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ORNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInstalments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentInstalments_PaymentRecords_PaymentRecordId",
                        column: x => x.PaymentRecordId,
                        principalTable: "PaymentRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstalments_MunicipalityId_ORNumber",
                table: "PaymentInstalments",
                columns: new[] { "MunicipalityId", "ORNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstalments_MunicipalityId_PaymentRecordId",
                table: "PaymentInstalments",
                columns: new[] { "MunicipalityId", "PaymentRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstalments_MunicipalityId_ReceivedAt",
                table: "PaymentInstalments",
                columns: new[] { "MunicipalityId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstalments_PaymentRecordId",
                table: "PaymentInstalments",
                column: "PaymentRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentInstalments");
        }
    }
}
