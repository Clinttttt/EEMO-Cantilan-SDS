using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectorOperationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_MunicipalityId_Id",
                table: "Users",
                columns: new[] { "MunicipalityId", "Id" });

            migrationBuilder.CreateTable(
                name: "CollectorOperationAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectorOperationAssignments", x => x.Id);
                    table.CheckConstraint("CK_CollectorOperationAssignments_OperationCode", "\"OperationCode\" ~ '^[A-Z0-9_]{1,64}$'");
                    table.ForeignKey(
                        name: "FK_CollectorOperationAssignments_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectorOperationAssignments_Users_MunicipalityId_Collecto~",
                        columns: x => new { x.MunicipalityId, x.CollectorId },
                        principalTable: "Users",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectorOperationAssignments_MunicipalityId_CollectorId_Op~",
                table: "CollectorOperationAssignments",
                columns: new[] { "MunicipalityId", "CollectorId", "OperationCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectorOperationAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_MunicipalityId_Id",
                table: "Users");
        }
    }
}
