using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentOneOpenCheckout20261104 : Migration
    {
        /// <summary>
        /// Production data could not be checked when this was written, and a duplicate would otherwise fail the release.
        /// shortcut: with duplicates the index is skipped with a warning; run this again once the reconciler has closed them.
        /// </summary>
        public const string CreateIndexUnlessDuplicatesExist = """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "OnlinePaymentTransactions"
                    WHERE "PaymentRecordId" IS NOT NULL AND "Status" IN (1, 2) AND "IsDeleted" = false
                    GROUP BY "PaymentRecordId"
                    HAVING COUNT(*) > 1)
                THEN
                    RAISE WARNING 'UX_OnlinePaymentTransactions_OneOpenCheckoutPerRecord was not created: a period has more than one open checkout.';
                ELSE
                    CREATE UNIQUE INDEX IF NOT EXISTS "UX_OnlinePaymentTransactions_OneOpenCheckoutPerRecord"
                        ON "OnlinePaymentTransactions" ("PaymentRecordId")
                        WHERE "PaymentRecordId" IS NOT NULL AND "Status" IN (1, 2) AND "IsDeleted" = false;
                END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CreateIndexUnlessDuplicatesExist);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_OnlinePaymentTransactions_OneOpenCheckoutPerRecord\";");
        }
    }
}
