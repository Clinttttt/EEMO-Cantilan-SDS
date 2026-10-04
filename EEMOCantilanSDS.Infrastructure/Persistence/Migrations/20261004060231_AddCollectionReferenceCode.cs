using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionReferenceCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "CollectionReferenceNumberSeq");

            // 1. Add the identity columns nullable so existing rows can be numbered deterministically before the constraints apply.
            migrationBuilder.AddColumn<long>(name: "ReferenceNumber", table: "Collections", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ReferenceYear", table: "Collections", type: "integer", nullable: true);

            // 2. Deterministic, additive backfill of EXISTING collections: one global order (recorded time, then id), numbered 1..N, with
            //    the year the collection was recorded in Philippine time. Nothing else about a collection is touched.
            migrationBuilder.Sql(
                "WITH ordered AS (SELECT \"Id\", ROW_NUMBER() OVER (ORDER BY \"RecordedAtUtc\", \"Id\") AS rn FROM \"Collections\") " +
                "UPDATE \"Collections\" c SET \"ReferenceNumber\" = o.rn, " +
                "\"ReferenceYear\" = EXTRACT(YEAR FROM c.\"RecordedAtUtc\" AT TIME ZONE 'Asia/Manila')::int " +
                "FROM ordered o WHERE o.\"Id\" = c.\"Id\";");

            // 3. New collections continue the same sequence from the next number; it never restarts.
            migrationBuilder.Sql(
                "SELECT setval('\"CollectionReferenceNumberSeq\"', COALESCE((SELECT MAX(\"ReferenceNumber\") FROM \"Collections\"), 0) + 1, false);");

            migrationBuilder.AlterColumn<long>(
                name: "ReferenceNumber", table: "Collections", type: "bigint", nullable: false,
                defaultValueSql: "nextval('\"CollectionReferenceNumberSeq\"')", oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
            migrationBuilder.AlterColumn<int>(
                name: "ReferenceYear", table: "Collections", type: "integer", nullable: false, defaultValue: 0,
                oldClrType: typeof(int), oldType: "integer", oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCode",
                table: "Collections",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                computedColumnSql: "'SRC-' || \"ReferenceYear\"::text || '-' || repeat('0', greatest(6 - length(\"ReferenceNumber\"::text), 0)) || \"ReferenceNumber\"::text",
                stored: true);

            migrationBuilder.CreateIndex(name: "IX_Collections_ReferenceCode", table: "Collections", column: "ReferenceCode", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Collections_ReferenceNumber", table: "Collections", column: "ReferenceNumber", unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collections_ReferenceCode",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_ReferenceNumber",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ReferenceCode",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ReferenceYear",
                table: "Collections");

            migrationBuilder.DropSequence(
                name: "CollectionReferenceNumberSeq");
        }
    }
}
