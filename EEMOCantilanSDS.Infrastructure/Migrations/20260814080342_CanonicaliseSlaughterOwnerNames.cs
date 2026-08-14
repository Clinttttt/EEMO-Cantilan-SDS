using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <summary>
    /// Brings stored slaughterhouse owner names into the canonical form the domain now writes: outer whitespace trimmed and
    /// internal runs of whitespace collapsed to a single space.
    ///
    /// <para>
    /// There is no schema change here, only data. It is needed because an owner IS the typed name — there is no client record
    /// behind it — so a row holding "Juan  Dela Cruz" is a different client from "Juan Dela Cruz" for the purposes of the
    /// owner picker, which now offers canonical names only. Without this, a name entered with a stray double space before the
    /// fix would no longer be reachable from the picker and its transactions would sit outside that client's totals.
    /// </para>
    ///
    /// <para>
    /// Capitalisation is deliberately left alone: the office's documents print the name as the clerk typed it, and matching
    /// already ignores case. Whitespace carries no such meaning, so removing the redundancy discards nothing the office
    /// entered on purpose.
    /// </para>
    ///
    /// <para>Idempotent: running it on already-canonical data changes no rows.</para>
    /// </summary>
    public partial class CanonicaliseSlaughterOwnerNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // btrim removes leading/trailing whitespace; regexp_replace with 'g' collapses every internal run. The WHERE
            // limits the write to rows that actually differ, so a re-run is a no-op rather than a full-table rewrite.
            migrationBuilder.Sql(@"
                UPDATE ""SlaughterTransactions""
                SET ""OwnerName"" = btrim(regexp_replace(""OwnerName"", '\s+', ' ', 'g'))
                WHERE ""OwnerName"" IS NOT NULL
                  AND ""OwnerName"" <> btrim(regexp_replace(""OwnerName"", '\s+', ' ', 'g'));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The redundant whitespace that was removed is not recorded anywhere, so it cannot be put
            // back; there is also nothing to put back that any office process depends on. Reverting the code alone restores
            // the previous behaviour.
        }
    }
}
