-- Additive migration 20261019090000_AddSlaughterChargeScope20261019 (idempotent; no data is rewritten).
-- Two nullable columns on the approved fee options: the animals a Slaughterhouse charge applies to, and whether its quantity is the
-- head count or a unit count. Every existing fee option keeps NULL (not animal-specific). Apply with ON_ERROR_STOP; never via Migrate.
BEGIN;
ALTER TABLE "GovernedServiceFeeOptions" ADD COLUMN IF NOT EXISTS "ApplicableAnimals" integer NULL;
ALTER TABLE "GovernedServiceFeeOptions" ADD COLUMN IF NOT EXISTS "QuantityBasis" integer NULL;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261019090000_AddSlaughterChargeScope20261019', '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;
COMMIT;
