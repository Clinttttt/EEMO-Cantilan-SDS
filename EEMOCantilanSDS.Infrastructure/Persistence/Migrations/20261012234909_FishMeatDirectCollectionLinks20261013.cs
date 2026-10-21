using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FishMeatDirectCollectionLinks20261013 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FishMeatDirectCollectionLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FishMeatDirectCollectionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FishMeatDirectCollectionLinks_Collections_MunicipalityId_Co~",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FishMeatDirectCollectionLinks_FishMeatVendorRegistrations_M~",
                        columns: x => new { x.MunicipalityId, x.VendorRegistrationId },
                        principalTable: "FishMeatVendorRegistrations",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatDirectCollectionLinks_MunicipalityId_ClientOperatio~",
                table: "FishMeatDirectCollectionLinks",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatDirectCollectionLinks_MunicipalityId_CollectionId",
                table: "FishMeatDirectCollectionLinks",
                columns: new[] { "MunicipalityId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FishMeatDirectCollectionLinks_MunicipalityId_VendorRegistra~",
                table: "FishMeatDirectCollectionLinks",
                columns: new[] { "MunicipalityId", "VendorRegistrationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FishMeatDirectCollectionLinks");
        }
    }
}
