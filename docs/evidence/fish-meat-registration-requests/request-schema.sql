-- OFFLINE REVIEW ONLY. No active migration or authorization to install.
CREATE TABLE "FishMeatRegistrationRequests" (
    "Id" uuid NOT NULL,
    "MunicipalityId" uuid NOT NULL,
    "CollectorId" uuid NOT NULL,
    "SubmittedBy" character varying(100) NOT NULL,
    "ClientOperationId" uuid NOT NULL,
    "Fingerprint" character varying(64) NOT NULL,
    "PendingKey" character varying(64) NOT NULL,
    "VendorName" character varying(200) NOT NULL,
    "VendorType" integer NOT NULL,
    "TaxYear" integer NOT NULL,
    "Contact" character varying(200),
    "Note" character varying(1000),
    "SubmittedAtUtc" timestamp with time zone NOT NULL,
    "ReplacesRequestId" uuid,
    "Status" integer NOT NULL,
    "Version" integer NOT NULL,
    "DecisionOperationId" uuid,
    "DecisionFingerprint" character varying(64),
    "RegistrationId" uuid,
    "ReviewedBy" character varying(100),
    "ReviewedByUserId" uuid,
    "ReviewedAtUtc" timestamp with time zone,
    "Reason" character varying(1000),
    CONSTRAINT "PK_FishMeatRegistrationRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_FishMeatRegistrationRequests_MunicipalityId_Id" UNIQUE ("MunicipalityId", "Id"),
    CONSTRAINT "CK_FishMeatRequest_Identity" CHECK ("TaxYear" BETWEEN 2000 AND 2200 AND "VendorType" IN (1,2) AND length(btrim("VendorName")) > 0),
    CONSTRAINT "CK_FishMeatRequest_ReviewEvidence" CHECK (("Status" = 1 AND "ReviewedByUserId" IS NULL) OR ("Status" <> 1 AND "ReviewedByUserId" IS NOT NULL AND "Reason" IS NOT NULL)),
    CONSTRAINT "CK_FishMeatRequest_State" CHECK ("Status" IN (1,2,3,4,5) AND (("Status" = 1 AND "Version" = 1 AND "DecisionOperationId" IS NULL AND "DecisionFingerprint" IS NULL AND "ReviewedAtUtc" IS NULL AND "ReviewedBy" IS NULL AND "Reason" IS NULL) OR ("Status" <> 1 AND "Version" = 2 AND "DecisionOperationId" IS NOT NULL AND "DecisionFingerprint" IS NOT NULL AND "ReviewedAtUtc" IS NOT NULL AND "ReviewedBy" IS NOT NULL AND length(btrim("Reason")) > 0)) AND (("Status" = 2 AND "RegistrationId" IS NOT NULL) OR ("Status" <> 2 AND "RegistrationId" IS NULL))),
    CONSTRAINT "FK_FishMeatRegistrationRequests_FishMeatRegistrationRequests_M~" FOREIGN KEY ("MunicipalityId", "ReplacesRequestId") REFERENCES "FishMeatRegistrationRequests" ("MunicipalityId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FishMeatRegistrationRequests_FishMeatVendorRegistrations_Mu~" FOREIGN KEY ("MunicipalityId", "RegistrationId") REFERENCES "FishMeatVendorRegistrations" ("MunicipalityId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FishMeatRegistrationRequests_Users_MunicipalityId_Collector~" FOREIGN KEY ("MunicipalityId", "CollectorId") REFERENCES "Users" ("MunicipalityId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FishMeatRegistrationRequests_Users_MunicipalityId_ReviewedB~" FOREIGN KEY ("MunicipalityId", "ReviewedByUserId") REFERENCES "Users" ("MunicipalityId", "Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_ClientOperation~" ON "FishMeatRegistrationRequests" ("MunicipalityId", "ClientOperationId");

CREATE UNIQUE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_CollectorId_Pen~" ON "FishMeatRegistrationRequests" ("MunicipalityId", "CollectorId", "PendingKey") WHERE "Status" = 1;

CREATE UNIQUE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_DecisionOperati~" ON "FishMeatRegistrationRequests" ("MunicipalityId", "DecisionOperationId") WHERE "DecisionOperationId" IS NOT NULL;

CREATE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_RegistrationId" ON "FishMeatRegistrationRequests" ("MunicipalityId", "RegistrationId");

CREATE UNIQUE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_ReplacesRequest~" ON "FishMeatRegistrationRequests" ("MunicipalityId", "ReplacesRequestId") WHERE "ReplacesRequestId" IS NOT NULL;

CREATE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_ReviewedByUserId" ON "FishMeatRegistrationRequests" ("MunicipalityId", "ReviewedByUserId");

CREATE INDEX "IX_FishMeatRegistrationRequests_MunicipalityId_Status_Submitte~" ON "FishMeatRegistrationRequests" ("MunicipalityId", "Status", "SubmittedAtUtc", "Id");
