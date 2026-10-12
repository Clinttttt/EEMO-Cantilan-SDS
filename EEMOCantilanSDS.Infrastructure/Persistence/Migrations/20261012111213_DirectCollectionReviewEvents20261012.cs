using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectCollectionReviewEvents20261012 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectCollectionReviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectCollectionReviewEvents", x => x.Id);
                    table.CheckConstraint("CK_DirectCollectionReview_Note", "(\"Outcome\" <> 2) OR (\"Note\" IS NOT NULL AND length(btrim(\"Note\")) > 0)");
                    table.ForeignKey(
                        name: "FK_DirectCollectionReviewEvents_Collections_MunicipalityId_Col~",
                        columns: x => new { x.MunicipalityId, x.CollectionId },
                        principalTable: "Collections",
                        principalColumns: new[] { "MunicipalityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectCollectionReviewEvents_MunicipalityId_ClientOperation~",
                table: "DirectCollectionReviewEvents",
                columns: new[] { "MunicipalityId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectCollectionReviewEvents_MunicipalityId_CollectionId_Oc~",
                table: "DirectCollectionReviewEvents",
                columns: new[] { "MunicipalityId", "CollectionId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectCollectionReviewEvents");
        }
    }
}
