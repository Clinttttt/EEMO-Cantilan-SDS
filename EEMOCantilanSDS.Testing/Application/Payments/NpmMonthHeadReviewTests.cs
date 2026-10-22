using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Payments;

public sealed class NpmMonthHeadReviewTests
{
    [Fact]
    public async Task Approved_credit_is_audited_once_and_changed_retry_cannot_create_cash()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var request = new NpmRentMonthCreditRequest(Guid.NewGuid(), f.Stall.Id, f.Month.PeriodStart,
            f.Month.SettlementVersion, 25, "Reviewed office decision", "Approved credit evidence");
        var result = await f.Workflow.ApplyReviewedCreditAsync(request);
        Assert.True(result.IsSuccess, result.Error);
        var replay = await f.Workflow.ApplyReviewedCreditAsync(request);
        Assert.True(replay.IsSuccess, replay.Error); Assert.True(replay.Value!.Existing);
        Assert.False((await f.Workflow.ApplyReviewedCreditAsync(request with { ApprovedCredits = 30 })).IsSuccess);
        Assert.Equal(875, (await f.Quote()).Value!.Outstanding);
        Assert.Single(await f.Db.AuditLogs.ToListAsync());
        Assert.Empty(await f.Db.Collections.ToListAsync());
        Assert.Empty(await f.Db.PostingOperations.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Disabled_runtime_and_collectors_cannot_review_or_credit(bool enabled, bool collector)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(enabled: enabled, asCollector: collector);
        Assert.False((await f.Workflow.ReviewHeadAsync(f.Stall.Id, f.Month.PeriodStart)).IsSuccess);
        Assert.False((await f.Workflow.ApplyReviewedCreditAsync(new(Guid.NewGuid(), f.Stall.Id,
            f.Month.PeriodStart, f.Month.SettlementVersion, 25, "Office", "Evidence"))).IsSuccess);
        Assert.Empty(await f.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Reviewed_reversal_requires_current_version_and_replays_without_another_correction()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25);
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        var request = new NpmRentMonthReverseRequest(Guid.NewGuid(), posted.Value!.CollectionId, f.Stall.Id,
            f.Month.PeriodStart, f.Month.SettlementVersion - 1, "Verified entry correction");
        Assert.False((await f.Workflow.ReverseReviewedAsync(request)).IsSuccess);
        request = request with { ExpectedVersion = f.Month.SettlementVersion };
        Assert.True((await f.Workflow.ReverseReviewedAsync(request)).IsSuccess);
        Assert.True((await f.Workflow.ReverseReviewedAsync(request)).Value!.Existing);
        Assert.Single(await f.Db.CollectionCorrections.ToListAsync());
        Assert.Single(await f.Db.Collections.ToListAsync());
        Assert.Equal(25, (await f.Quote()).Value!.Outstanding);
    }

    [Fact]
    public async Task Current_day_verified_replacement_preserves_original_cash_and_links_one_new_collection()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25, partPayments: true);
        var original = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(original.IsSuccess, original.Error);
        var request = new NpmRentMonthReplacementRequest(Guid.NewGuid(), original.Value!.CollectionId,
            f.Month.SettlementVersion, 20, "Office review", "Corrected current-day entry");
        var result = await f.Workflow.ReplaceReviewedAsync(request);
        Assert.True(result.IsSuccess, result.Error);
        var replay = await f.Workflow.ReplaceReviewedAsync(request);
        Assert.True(replay.IsSuccess, replay.Error); Assert.True(replay.Value!.Existing);
        Assert.False((await f.Workflow.ReplaceReviewedAsync(request with { Amount = 15 })).IsSuccess);
        Assert.Equal(2, await f.Db.Collections.CountAsync());
        Assert.Equal(25, (await f.Db.Collections.SingleAsync(x => x.Id == original.Value.CollectionId)).TotalAmount);
        Assert.Equal(result.Value!.CollectionId, (await f.Db.CollectionCorrections.SingleAsync()).ReplacementCollectionId);
        Assert.Equal(5, (await f.Quote()).Value!.Outstanding);
        // InMemory proves intent/amount routing only, not transactional rollback or concurrent posting.
    }

    [Fact]
    public async Task Office_replacement_cannot_move_collector_cash_into_office_custody()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25, asCollector: true);
        var original = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(original.IsSuccess, original.Error);
        Moq.Mock.Get(f.User).SetupGet(x => x.Role).Returns("SuperAdmin");
        Moq.Mock.Get(f.User).SetupGet(x => x.CollectorId).Returns((Guid?)null);
        var result = await f.Workflow.ReplaceReviewedAsync(new(Guid.NewGuid(), original.Value!.CollectionId,
            f.Month.SettlementVersion, 25, "Office", "Correction review"));
        Assert.False(result.IsSuccess); Assert.Contains("custody", result.Error);
        Assert.Empty(await f.Db.CollectionCorrections.ToListAsync());
        Assert.Single(await f.Db.Collections.ToListAsync());
    }

    [Fact]
    public async Task Replacement_without_partial_authority_fails_before_reversing_original()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25);
        var original = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        var result = await f.Workflow.ReplaceReviewedAsync(new(Guid.NewGuid(), original.Value!.CollectionId,
            f.Month.SettlementVersion, 20, "Office", "Review"));
        Assert.False(result.IsSuccess);
        Assert.Empty(await f.Db.CollectionCorrections.ToListAsync());
        Assert.Single(await f.Db.Collections.ToListAsync());
    }
}
