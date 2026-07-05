using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillEmptyMunicipalityIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Self-healing safety backfill: stamp any tenant-owned row still carrying an empty MunicipalityId
            // with the DEFAULT municipality's id. This fixes rows that were created/imported before the
            // default existed (otherwise the per-request query filter hides them once a default is set).
            // Idempotent + safe: touches ONLY empty rows, and no-ops entirely when there is no default yet
            // (a fresh DB seeds its default at runtime; new rows are stamped by the write interceptor).
            migrationBuilder.Sql(@"
DO $$
DECLARE
    default_id uuid;
    r RECORD;
BEGIN
    SELECT ""Id"" INTO default_id FROM ""Municipalities"" WHERE ""IsDefault"" = true LIMIT 1;
    IF default_id IS NULL THEN
        RETURN;
    END IF;

    FOR r IN
        SELECT table_name
        FROM information_schema.columns
        WHERE column_name = 'MunicipalityId' AND table_schema = 'public'
    LOOP
        EXECUTE format(
            'UPDATE %I SET ""MunicipalityId"" = %L WHERE ""MunicipalityId"" = %L',
            r.table_name, default_id, '00000000-0000-0000-0000-000000000000');
    END LOOP;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill; nothing to reverse (the previous empty ids carried no tenant meaning).
        }
    }
}
