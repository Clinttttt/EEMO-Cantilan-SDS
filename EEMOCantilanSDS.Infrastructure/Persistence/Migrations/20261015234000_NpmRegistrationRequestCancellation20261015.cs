using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261015234000_NpmRegistrationRequestCancellation20261015")]
public sealed class NpmRegistrationRequestCancellation20261015 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "NpmRegistrationRequests" DROP CONSTRAINT "CK_NpmRegistrationRequest_State";
            ALTER TABLE "NpmRegistrationRequests" ADD CONSTRAINT "CK_NpmRegistrationRequest_State"
            CHECK ("Status" IN (1,2,3,4,5) AND "Version" > 0
            AND (("Status" = 1 AND "DecisionOperationId" IS NULL AND "ReviewedAtUtc" IS NULL AND "AssociationId" IS NULL)
              OR ("Status" <> 1 AND "DecisionOperationId" IS NOT NULL AND "ReviewedAtUtc" IS NOT NULL AND "ReviewedBy" IS NOT NULL AND "ReviewReason" IS NOT NULL))
            AND (("Status" = 2 AND "AssociationId" IS NOT NULL) OR ("Status" <> 2 AND "AssociationId" IS NULL)));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
              IF EXISTS (SELECT 1 FROM "NpmRegistrationRequests" WHERE "Status" = 5) THEN
                RAISE EXCEPTION 'Cannot remove cancellation status while cancelled NPM requests remain; preserve audit history';
              END IF;
            END $$;
            ALTER TABLE "NpmRegistrationRequests" DROP CONSTRAINT "CK_NpmRegistrationRequest_State";
            ALTER TABLE "NpmRegistrationRequests" ADD CONSTRAINT "CK_NpmRegistrationRequest_State"
            CHECK ("Status" IN (1,2,3,4) AND "Version" > 0
            AND (("Status" = 1 AND "DecisionOperationId" IS NULL AND "ReviewedAtUtc" IS NULL AND "AssociationId" IS NULL)
              OR ("Status" <> 1 AND "DecisionOperationId" IS NOT NULL AND "ReviewedAtUtc" IS NOT NULL AND "ReviewedBy" IS NOT NULL AND "ReviewReason" IS NOT NULL))
            AND (("Status" = 2 AND "AssociationId" IS NOT NULL) OR ("Status" <> 2 AND "AssociationId" IS NULL)));
            """);
    }
}
