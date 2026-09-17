using EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The PostgreSQL partial index closes races that application pre-checks cannot serialize.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ActiveOnboardingPipelineUniquenessTests(PostgresFixture db)
{
    private const string MigrationId = "20260918090000_EnforceSingleActiveOnboardingPipeline";
    private static readonly Guid StaleCarmenRequestId = Guid.Parse("bb751b0c-b0af-4642-b8bc-b0e5ac846ed3");
    private static readonly Guid CurrentCarmenRequestId = Guid.Parse("80223b1d-4837-4157-9433-05d163beca5e");

    [SkippableFact]
    public async Task Migration_WithNoAssessmentRows_SucceedsAndSecondMigrateIsANoOp()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await using var migrate = db.CreateContext(Guid.Empty);
            await migrate.Database.MigrateAsync();
            await migrate.Database.MigrateAsync();

            Assert.Equal(1, (await migrate.Database.GetAppliedMigrationsAsync()).Count(x => x == MigrationId));
            Assert.Equal(1, await IndexCountAsync());
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_WithNormalExistingDataAndNoDuplicates_LeavesDataUnchanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await using (var setup = db.CreateContext(Guid.Empty))
            {
                var declinedCarmen = Request("Carmen", "Surigao del Sur", "old@carmen.gov.ph");
                declinedCarmen.Decline("Not ready", "operator");
                setup.AssessmentRequests.AddRange(
                    declinedCarmen,
                    Request("Carmen", "Surigao del Sur", "new@carmen.gov.ph"),
                    Request("Madrid", "Surigao del Sur", "office@madrid.gov.ph"));
                await setup.SaveChangesAsync();
            }

            await using (var migrate = db.CreateContext(Guid.Empty)) await migrate.Database.MigrateAsync();

            await using var verify = db.CreateContext(Guid.Empty);
            var rows = await verify.AssessmentRequests.OrderBy(r => r.OfficialEmail).ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.Single(rows, r => r.Status == AssessmentRequestStatus.Declined);
            Assert.Equal(2, rows.Count(r => r.Status == AssessmentRequestStatus.PendingReview));
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_ExactReviewedCarmenPair_SupersedesOnlyStaleOnboarding()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await SeedReviewedCarmenPairAsync();
            await using (var migrate = db.CreateContext(Guid.Empty)) await migrate.Database.MigrateAsync();

            await using var verify = db.CreateContext(Guid.Empty);
            var current = await verify.AssessmentRequests.SingleAsync(r => r.Id == CurrentCarmenRequestId);
            var stale = await verify.AssessmentRequests.SingleAsync(r => r.Id == StaleCarmenRequestId);
            Assert.Equal(AssessmentRequestStatus.Approved, current.Status);
            Assert.Equal("Validation", current.Stage);
            Assert.Equal(AssessmentRequestStatus.Superseded, stale.Status);
            Assert.Equal("Superseded", stale.Stage);
            Assert.Equal(2, await verify.AssessmentRequests.CountAsync());
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_UnknownDuplicateGroup_FailsWithoutChoosingAWinner()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await using (var setup = db.CreateContext(Guid.Empty))
            {
                setup.AssessmentRequests.AddRange(
                    Request("Madrid", "Surigao del Sur", "first@madrid.gov.ph"),
                    Request(" madrid ", "SURIGAO DEL SUR", "second@madrid.gov.ph"));
                await setup.SaveChangesAsync();
            }

            await using var migrate = db.CreateContext(Guid.Empty);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => migrate.Database.MigrateAsync());
            Assert.Contains("Migration aborted for manual review", error.ToString());

            await using var verify = db.CreateContext(Guid.Empty);
            Assert.Equal(2, await verify.AssessmentRequests.CountAsync(r => r.Status == AssessmentRequestStatus.PendingReview));
            Assert.DoesNotContain(MigrationId, await verify.Database.GetAppliedMigrationsAsync());
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_KnownCarmenIdsWithUnexpectedLifecycle_FailsWithoutChangingEitherRequest()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await SeedReviewedCarmenPairAsync(currentStageIsValidation: false);

            await using var migrate = db.CreateContext(Guid.Empty);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => migrate.Database.MigrateAsync());
            Assert.Contains("not the exact reviewed Carmen Onboarding/Validation pair", error.ToString());

            await using var verify = db.CreateContext(Guid.Empty);
            var rows = await verify.AssessmentRequests.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(AssessmentRequestStatus.Approved, r.Status));
            Assert.DoesNotContain(rows, r => r.Status == AssessmentRequestStatus.Superseded);
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_ReviewedPairWithLiveCarmen_FailsWithoutChangingEitherRequest()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await SeedReviewedCarmenPairAsync(carmenIsLive: true);

            await using var migrate = db.CreateContext(Guid.Empty);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => migrate.Database.MigrateAsync());
            Assert.Contains("Carmen is missing, duplicated, or already live", error.ToString());

            await using var verify = db.CreateContext(Guid.Empty);
            Assert.Equal(2, await verify.AssessmentRequests.CountAsync(r => r.Status == AssessmentRequestStatus.Approved));
            Assert.DoesNotContain(MigrationId, await verify.Database.GetAppliedMigrationsAsync());
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task Migration_ReviewedPairWithoutSubmittedDraft_FailsWithoutChangingEitherRequest()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await RewindMigrationAsync();

        try
        {
            await SeedReviewedCarmenPairAsync(includeSubmittedDraft: false);

            await using var migrate = db.CreateContext(Guid.Empty);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => migrate.Database.MigrateAsync());
            Assert.Contains("no longer has a submitted onboarding draft", error.ToString());

            await using var verify = db.CreateContext(Guid.Empty);
            Assert.Equal(2, await verify.AssessmentRequests.CountAsync(r => r.Status == AssessmentRequestStatus.Approved));
            Assert.DoesNotContain(MigrationId, await verify.Database.GetAppliedMigrationsAsync());
        }
        finally { await RestoreLatestSchemaAsync(); }
    }

    [SkippableFact]
    public async Task SubmissionGuard_ReturnsTheExistingNormalizedMunicipalityAndStage()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        await using var context = db.CreateContext(Guid.Empty);
        var active = Request("Carmen", "Surigao del Sur", "first@carmen.gov.ph");
        active.Approve("https://www.stalltrack.site/onboarding/carmen", null, "operator");
        active.SubmitForValidation("LGU");
        context.AssessmentRequests.Add(active);
        await context.SaveChangesAsync();

        var result = await new SubmitAssessmentRequestCommandHandler(context).Handle(
            new SubmitAssessmentRequestCommand(
                " carmen ", "SURIGAO DEL SUR", "Municipality of Carmen", "Second", "Officer",
                "second@carmen.gov.ph", "0913", string.Empty, null, null, true, null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("Carmen already has an active onboarding request in Validation.", result.Error);
    }

    [SkippableFact]
    public async Task ConcurrentCaseVariantSubmissions_CannotCreateTwoActivePipelines()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        await using var first = db.CreateContext(Guid.Empty);
        await using var second = db.CreateContext(Guid.Empty);
        first.AssessmentRequests.Add(Request("Carmen", "Surigao del Sur", "first@carmen.gov.ph"));
        second.AssessmentRequests.Add(Request(" carmen ", "SURIGAO DEL SUR", "second@carmen.gov.ph"));

        var results = await Task.WhenAll(TrySaveAsync(first), TrySaveAsync(second));

        Assert.Single(results, x => x.saved);
        var failure = Assert.Single(results, x => !x.saved).error!;
        Assert.Contains("UX_AssessmentRequests_ActiveMunicipality", failure.InnerException?.Message ?? failure.Message);
    }

    [SkippableFact]
    public async Task HistoricalAndDifferentMunicipalities_DoNotConflict()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        await using var context = db.CreateContext(Guid.Empty);
        var declinedCarmen = Request("Carmen", "Surigao del Sur", "old@carmen.gov.ph");
        declinedCarmen.Decline("Not ready", "operator");
        context.AssessmentRequests.AddRange(
            declinedCarmen,
            Request("Carmen", "Surigao del Sur", "new@carmen.gov.ph"),
            Request("Carmen", "Agusan del Norte", "other@carmen.gov.ph"),
            Request("Madrid", "Surigao del Sur", "office@madrid.gov.ph"));

        await context.SaveChangesAsync();

        Assert.Equal(4, await context.AssessmentRequests.CountAsync());
    }

    private async Task SeedReviewedCarmenPairAsync(
        bool currentStageIsValidation = true,
        bool carmenIsLive = false,
        bool includeSubmittedDraft = true)
    {
        await using var setup = db.CreateContext(Guid.Empty);

        var municipality = Municipality.Create(
            "CARMEN",
            "Carmen",
            "Surigao del Sur",
            carmenIsLive ? MunicipalityStatus.Active : MunicipalityStatus.Upcoming,
            tenantCode: "carmen",
            officeName: "Carmen Economic Enterprise Office");

        var stale = Request("Carmen", "Surigao del Sur", "old@carmen.gov.ph");
        SetId(stale, StaleCarmenRequestId);
        stale.Approve("https://www.stalltrack.site/onboarding/old", null, "operator");

        var current = Request("Carmen", "Surigao del Sur", "current@carmen.gov.ph");
        SetId(current, CurrentCarmenRequestId);
        current.Approve("https://www.stalltrack.site/onboarding/current", null, "operator");
        if (currentStageIsValidation) current.SubmitForValidation("LGU");

        setup.Municipalities.Add(municipality);
        setup.AssessmentRequests.AddRange(stale, current);

        if (includeSubmittedDraft)
        {
            var draft = OnboardingDraft.Create(
                CurrentCarmenRequestId,
                "Carmen",
                "Surigao del Sur",
                $"test-{Guid.NewGuid():N}",
                DateTime.UtcNow.AddDays(30));
            draft.SubmitForValidation("LGU");
            setup.OnboardingDrafts.Add(draft);
        }

        await setup.SaveChangesAsync();
    }

    private async Task RewindMigrationAsync()
    {
        await using var context = db.CreateContext(Guid.Empty);
        await context.Database.ExecuteSqlRawAsync(
            $"""
            DROP INDEX IF EXISTS "UX_AssessmentRequests_ActiveMunicipality";
            DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{MigrationId}';
            """);
    }

    private async Task RestoreLatestSchemaAsync()
    {
        await db.ResetAsync();
        await using var restore = db.CreateContext(Guid.Empty);
        await restore.Database.MigrateAsync();
    }

    private async Task<int> IndexCountAsync()
    {
        await using var connection = db.CreateRawConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'UX_AssessmentRequests_ActiveMunicipality';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void SetId(BaseEntity entity, Guid id)
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
    }

    private static AssessmentRequest Request(string municipality, string province, string email) =>
        AssessmentRequest.Create(municipality, province, $"Municipality of {municipality.Trim()}", "Focal Person",
            "Officer", email, "09123456789", string.Empty, null, null, true, null);

    private static async Task<(bool saved, DbUpdateException? error)> TrySaveAsync(DbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return (true, null);
        }
        catch (DbUpdateException error)
        {
            return (false, error);
        }
    }
}
