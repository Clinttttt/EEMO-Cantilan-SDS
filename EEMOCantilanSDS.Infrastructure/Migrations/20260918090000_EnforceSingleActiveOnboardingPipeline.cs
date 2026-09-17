using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260918090000_EnforceSingleActiveOnboardingPipeline")]
public class EnforceSingleActiveOnboardingPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Production had one verified pre-constraint duplicate. Remediate only that exact pair; a stage ordering is
        // not an authority rule, so any different duplicate shape must stop deployment for human review.
        migrationBuilder.Sql(
            """
            DO $migration$
            DECLARE
                duplicate_group_count integer;
                carmen_active_count integer;
                carmen_registry_count integer;
                inactive_carmen_registry_count integer;
                updated_count integer;
            BEGIN
                SELECT count(*)
                INTO duplicate_group_count
                FROM (
                    SELECT lower(btrim("Municipality")), lower(btrim("Province"))
                    FROM "AssessmentRequests"
                    WHERE NOT "IsDeleted"
                      AND ("Status" = 1 OR ("Status" = 2 AND "Stage" IN ('Onboarding', 'Validation', 'Activation')))
                    GROUP BY lower(btrim("Municipality")), lower(btrim("Province"))
                    HAVING count(*) > 1
                ) AS duplicate_groups;

                IF duplicate_group_count = 0 THEN
                    RETURN;
                END IF;

                IF duplicate_group_count <> 1 THEN
                    RAISE EXCEPTION
                        'Active onboarding duplicates differ from the single reviewed Carmen remediation (% duplicate groups found). Migration aborted for manual review.',
                        duplicate_group_count;
                END IF;

                SELECT count(*)
                INTO carmen_active_count
                FROM "AssessmentRequests"
                WHERE NOT "IsDeleted"
                  AND lower(btrim("Municipality")) = 'carmen'
                  AND lower(btrim("Province")) = 'surigao del sur'
                  AND ("Status" = 1 OR ("Status" = 2 AND "Stage" IN ('Onboarding', 'Validation', 'Activation')));

                IF carmen_active_count <> 2
                   OR NOT EXISTS (
                       SELECT 1
                       FROM "AssessmentRequests"
                       WHERE "Id" = 'bb751b0c-b0af-4642-b8bc-b0e5ac846ed3'::uuid
                         AND NOT "IsDeleted"
                         AND lower(btrim("Municipality")) = 'carmen'
                         AND lower(btrim("Province")) = 'surigao del sur'
                         AND "Status" = 2
                         AND "Stage" = 'Onboarding')
                   OR NOT EXISTS (
                       SELECT 1
                       FROM "AssessmentRequests"
                       WHERE "Id" = '80223b1d-4837-4157-9433-05d163beca5e'::uuid
                         AND NOT "IsDeleted"
                         AND lower(btrim("Municipality")) = 'carmen'
                         AND lower(btrim("Province")) = 'surigao del sur'
                         AND "Status" = 2
                         AND "Stage" = 'Validation') THEN
                    RAISE EXCEPTION
                        'The active onboarding duplicate is not the exact reviewed Carmen Onboarding/Validation pair. Migration aborted for manual review.';
                END IF;

                SELECT count(*), count(*) FILTER (WHERE NOT "IsActive")
                INTO carmen_registry_count, inactive_carmen_registry_count
                FROM "Municipalities"
                WHERE NOT "IsDeleted"
                  AND lower(btrim("Name")) = 'carmen'
                  AND lower(btrim("Province")) = 'surigao del sur';

                IF carmen_registry_count <> 1 OR inactive_carmen_registry_count <> 1 THEN
                    RAISE EXCEPTION
                        'Carmen is missing, duplicated, or already live. Migration aborted without superseding onboarding history.';
                END IF;

                IF NOT EXISTS (
                    SELECT 1
                    FROM "OnboardingDrafts"
                    WHERE "AssessmentRequestId" = '80223b1d-4837-4157-9433-05d163beca5e'::uuid
                      AND NOT "IsDeleted"
                      AND "IsSubmittedForValidation"
                      AND "SubmittedAt" IS NOT NULL) THEN
                    RAISE EXCEPTION
                        'The reviewed Carmen Validation request no longer has a submitted onboarding draft. Migration aborted without choosing a winner.';
                END IF;

                UPDATE "AssessmentRequests"
                SET "Status" = 5,
                    "Stage" = 'Superseded',
                    "DecisionMessage" = COALESCE(
                        "DecisionMessage",
                        'Superseded after review of the duplicate Carmen onboarding pipeline.'),
                    "UpdatedAt" = NOW(),
                    "UpdatedBy" = 'Active-pipeline integrity migration'
                WHERE "Id" = 'bb751b0c-b0af-4642-b8bc-b0e5ac846ed3'::uuid
                  AND NOT "IsDeleted"
                  AND lower(btrim("Municipality")) = 'carmen'
                  AND lower(btrim("Province")) = 'surigao del sur'
                  AND "Status" = 2
                  AND "Stage" = 'Onboarding';

                GET DIAGNOSTICS updated_count = ROW_COUNT;
                IF updated_count <> 1 THEN
                    RAISE EXCEPTION
                        'The reviewed stale Carmen request changed before remediation. Migration aborted.';
                END IF;
            END
            $migration$;

            CREATE UNIQUE INDEX "UX_AssessmentRequests_ActiveMunicipality"
            ON "AssessmentRequests" (lower(btrim("Municipality")), lower(btrim("Province")))
            WHERE NOT "IsDeleted"
              AND ("Status" = 1 OR ("Status" = 2 AND "Stage" IN ('Onboarding', 'Validation', 'Activation')));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_AssessmentRequests_ActiveMunicipality\";");
        // Superseded history is intentionally not guessed back into an active state on rollback.
    }
}
