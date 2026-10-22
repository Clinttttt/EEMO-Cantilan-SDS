START TRANSACTION;
ALTER TABLE "FishMeatVendorRegistrations" DROP CONSTRAINT "CK_VendorRegistration_Lifecycle";

ALTER TABLE "FishMeatVendorRegistrations" ADD "DeleteClientOperationId" uuid;

ALTER TABLE "FishMeatVendorRegistrations" ADD "DeleteReason" character varying(500);

ALTER TABLE "FishMeatVendorRegistrations" ADD "DeletedAtUtc" timestamp with time zone;

ALTER TABLE "FishMeatVendorRegistrations" ADD "DeletedBy" character varying(100);

CREATE UNIQUE INDEX "IX_FishMeatVendorRegistrations_MunicipalityId_DeleteClientOper~" ON "FishMeatVendorRegistrations" ("MunicipalityId", "DeleteClientOperationId") WHERE "DeleteClientOperationId" IS NOT NULL;

ALTER TABLE "FishMeatVendorRegistrations" ADD CONSTRAINT "CK_VendorRegistration_Deletion" CHECK (("Status" IN (1,2) AND "DeleteClientOperationId" IS NULL AND "DeletedAtUtc" IS NULL AND "DeletedBy" IS NULL AND "DeleteReason" IS NULL) OR ("Status" = 3 AND "DeleteClientOperationId" IS NOT NULL AND "DeletedAtUtc" IS NOT NULL AND "DeletedBy" IS NOT NULL AND "DeleteReason" IS NOT NULL AND length(btrim("DeletedBy")) > 0 AND length(btrim("DeleteReason")) > 0));

ALTER TABLE "FishMeatVendorRegistrations" ADD CONSTRAINT "CK_VendorRegistration_Lifecycle" CHECK (("Status" = 1 AND "CloseClientOperationId" IS NULL AND "ClosedOn" IS NULL AND "ClosedAtUtc" IS NULL AND "ClosedBy" IS NULL AND "CloseNote" IS NULL) OR ("Status" IN (2,3) AND "CloseClientOperationId" IS NOT NULL AND "ClosedOn" IS NOT NULL AND "ClosedAtUtc" IS NOT NULL AND "ClosedBy" IS NOT NULL));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261022124440_FishMeatRegistrationLogicalDeletion20261022', '9.0.4');

COMMIT;
