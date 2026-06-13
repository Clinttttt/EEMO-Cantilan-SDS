using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePaymentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnlinePaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PayorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    GatewayReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    ORNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_OnlinePaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlinePaymentTransactions_PaymentRecords_PaymentRecordId",
                        column: x => x.PaymentRecordId,
                        principalTable: "PaymentRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentTransactions_GatewayReference",
                table: "OnlinePaymentTransactions",
                column: "GatewayReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentTransactions_PaymentRecordId",
                table: "OnlinePaymentTransactions",
                column: "PaymentRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentTransactions_Reference",
                table: "OnlinePaymentTransactions",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlinePaymentTransactions");
        }
    }
}
