using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Month-aware contract duration for monthly Space Rentals, against real PostgreSQL: recording, validation, the accrual cut-off (ST-08), the
/// renewal chain, reassign, correction, import and tenant isolation. A yearly contract must read exactly as it did before months existed.
/// </summary>
public sealed partial class ObligationAccountTests
{
    private static CreateObligationAccountRequest SignedKm(Seed seed, DateOnly start, int? years, int? months, DateOnly? contractDate = null,
        ObligationKind kind = ObligationKind.KanmanggaySpaceRental) =>
        new(kind, seed.PayorId, null, "", null, null, start, 1200m, OccupancyArrangement.SignedContract, "REF-" + Guid.NewGuid().ToString("N")[..6],
            "Occupant " + Guid.NewGuid().ToString("N")[..6], "Lessee", contractDate ?? start, years, ContractMonths: months);

    private async Task<Guid> CreateSignedAsync(Seed seed, DateOnly start, int? years, int? months, DateOnly? contractDate = null,
        ObligationKind kind = ObligationKind.KanmanggaySpaceRental)
    {
        await using var context = db.CreateContext(seed.TenantId);
        var created = await Setup(context, seed).CreateAccountAsync(SignedKm(seed, start, years, months, contractDate, kind));
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!.Id;
    }

    private async Task<ObligationAccountDto> ReadAsync(Seed seed, Guid id, ObligationKind kind = ObligationKind.KanmanggaySpaceRental)
    {
        await using var context = db.CreateContext(seed.TenantId);
        return (await Setup(context, seed).GetAccountsAsync(kind)).Value!.Single(a => a.Id == id);
    }

    [SkippableFact]
    public async Task ContractMonths_AreRecordedCanonically_AndYearlyContractsAreUnchanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var monthsOnly = await ReadAsync(seed, await CreateSignedAsync(seed, new(2026, 2, 1), null, 6));
        Assert.Equal((0, 6), (monthsOnly.ContractYears, monthsOnly.ContractMonths));
        var yearly = await ReadAsync(seed, await CreateSignedAsync(seed, new(2026, 2, 1), 3, null));
        Assert.Equal(3, yearly.ContractYears);
        Assert.Null(yearly.ContractMonths);
        var twelve = await ReadAsync(seed, await CreateSignedAsync(seed, new(2026, 2, 1), null, 12));
        Assert.Equal((1, (int?)null), (twelve.ContractYears, twelve.ContractMonths));       // 12 months is one year, one record
        var mixed = await ReadAsync(seed, await CreateSignedAsync(seed, new(2026, 2, 1), 1, 6, kind: ObligationKind.VegetableFruitSpaceRental), ObligationKind.VegetableFruitSpaceRental);
        Assert.Equal((1, 6), (mixed.ContractYears, mixed.ContractMonths));

        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Equal(new int?[] { 6, null, null, 6 }, (await verify.ObligationAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).ToListAsync()).Select(a => a.ContractMonths).ToArray());
    }

    [SkippableTheory]
    [InlineData(null, 0)]           // a zero-length term
    [InlineData(null, -1)]
    [InlineData(0, null)]
    [InlineData(50, 1)]             // longer than 50 years
    [InlineData(null, 601)]
    [InlineData(51, null)]
    public async Task ContractMonths_InvalidTermsAreRefused_AndNothingIsSaved(int? years, int? months)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var result = await Setup(context, seed).CreateAccountAsync(SignedKm(seed, new(2026, 2, 1), years, months));
        Assert.False(result.IsSuccess);
        Assert.Equal(0, await context.ObligationAccounts.CountAsync());
    }

    [SkippableFact]
    public async Task ContractMonths_AnOfficeWithoutAConfirmedDurationPolicy_CannotRecordAMonthsTerm_ButSavesTheContractWithoutOne()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(defaultOffice: false);
        await using var context = db.CreateContext(seed.TenantId);
        var refused = await Setup(context, seed).CreateAccountAsync(SignedKm(seed, new(2026, 2, 1), null, 6));
        Assert.False(refused.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, refused.Status);
        var without = await Setup(context, seed).CreateAccountAsync(SignedKm(seed, new(2026, 2, 1), null, null));
        Assert.True(without.IsSuccess, without.Error);
    }

    [SkippableFact]
    public async Task ContractMonths_AccrualStopsAfterTheMonthBasedLastDay_WhileEarlierAndAlreadyAssessedMonthsStayCollectible()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var sixMonths = await CreateSignedAsync(seed, new(2026, 1, 1), null, 6);                       // last day 2026-06-30
        var midMonth = await CreateSignedAsync(seed, new(2026, 2, 1), null, 6, new(2026, 2, 15));      // last day 2026-08-14: August is still inside
        var oneYear = await CreateSignedAsync(seed, new(2025, 11, 1), 1, null);                        // last day 2026-10-31: unchanged yearly behaviour
        var threeMonths = await CreateSignedAsync(seed, new(2026, 5, 1), null, 3);                     // last day 2026-07-31
        await using (var context = db.CreateContext(seed.TenantId))
        {
            // September was assessed before the cut-off logic applied to it: an incurred month stays collectible.
            var account = await context.ObligationAccounts.SingleAsync(a => a.Id == threeMonths);
            var rate = await context.ObligationRates.FirstAsync(r => r.ObligationAccountId == threeMonths);
            context.ObligationPeriods.Add(ObligationPeriod.Assess(account, rate, new DateOnly(2026, 9, 1), "test"));
            await context.SaveChangesAsync();
        }
        await using var read = db.CreateContext(seed.TenantId);
        var accounts = await read.ObligationAccounts.AsNoTracking().ToListAsync();
        var quotes = await new ObligationCollectionSource(read).GetQuotesAsync(seed.TenantId, accounts, new DateOnly(2026, 10, 4), default, lookbackMonths: 120);
        List<DateOnly> Months(Guid id) => quotes.Where(q => q.AccountId == id).Select(q => q.PeriodStart).OrderBy(x => x).ToList();

        Assert.Equal(Enumerable.Range(1, 6).Select(m => new DateOnly(2026, m, 1)), Months(sixMonths));
        Assert.Equal(Enumerable.Range(2, 7).Select(m => new DateOnly(2026, m, 1)), Months(midMonth));
        Assert.Equal(12, Months(oneYear).Count);
        Assert.Equal(new DateOnly(2025, 11, 1), Months(oneYear).First());
        Assert.Equal(new DateOnly(2026, 10, 1), Months(oneYear).Last());
        Assert.Equal(new[] { new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 1) }, Months(threeMonths));
        Assert.All(quotes.Where(q => q.AccountId == sixMonths), q => Assert.True(q.CanAddToDraft));
    }

    [SkippableFact]
    public async Task ContractMonths_RenewalChain_AppendsMonthAwareTerms_AndTheAccrualFollowsTheLatestTerm()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var id = await CreateSignedAsync(seed, new(2025, 1, 1), null, 6);                                  // 2025-01-01 .. 2025-06-30
        await using var context = db.CreateContext(seed.TenantId);
        SpaceLifecycleWorkflow Lifecycle() => new(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId), new HistoricalClock());
        LifecycleCommand Renew(DateOnly start, int? years, int? months) => new(LifecycleAction.Renew, start, "Renewal approved", "Res. 12", TermYears: years, TermMonths: months);

        // Refusals first: no term, a zero-length term, an overlap of the term still recorded.
        Assert.False((await Lifecycle().PreviewAsync(id, Renew(new(2025, 7, 1), null, null))).Value!.CanExecute);
        Assert.False((await Lifecycle().PreviewAsync(id, Renew(new(2025, 7, 1), null, 0))).Value!.CanExecute);
        Assert.Equal("RenewalOverlapsTerm", (await Lifecycle().PreviewAsync(id, Renew(new(2025, 6, 30), null, 6))).Value!.ReasonCode);

        var first = Renew(new(2025, 7, 1), null, 6);                                                       // 6 months more: ends 2025-12-31
        var preview = (await Lifecycle().PreviewAsync(id, first)).Value!;
        Assert.True(preview.CanExecute, preview.Explanation);
        Assert.Contains("6 months", preview.Facts["New term"]);
        var op = Guid.NewGuid();
        var done = await Lifecycle().ExecuteAsync(id, new(op, preview.Version, first));
        Assert.True(done.IsSuccess, done.Error);
        // The same operation id with a different term is a conflict, never a second silent term.
        var changed = Renew(new(2025, 7, 1), null, 5);
        Assert.False((await Lifecycle().ExecuteAsync(id, new(op, preview.Version, changed))).IsSuccess);

        var second = Renew(new(2026, 1, 1), 1, 6);                                                         // 18 months: ends 2027-06-30
        var secondPreview = (await Lifecycle().PreviewAsync(id, second)).Value!;
        Assert.True(secondPreview.CanExecute, secondPreview.Explanation);
        Assert.Contains("1 year 6 months", secondPreview.Facts["New term"]);
        Assert.True((await Lifecycle().ExecuteAsync(id, new(Guid.NewGuid(), secondPreview.Version, second))).IsSuccess);

        var terms = await context.SpaceContractTerms.AsNoTracking().OrderBy(t => t.Commencement).ToListAsync();
        Assert.Equal(2, terms.Count);
        Assert.Equal(((0, (int?)6), new DateOnly(2025, 12, 31)), ((terms[0].Years, terms[0].Months), terms[0].LastDay));
        Assert.Equal(((1, (int?)6), new DateOnly(2027, 6, 30)), ((terms[1].Years, terms[1].Months), terms[1].LastDay));
        Assert.Equal(terms[0].Id, terms[1].PriorTermId);

        var dto = await ReadAsync(seed, id);
        Assert.Equal((1, 6), (dto.ContractYears, dto.ContractMonths));                                     // the term in force
        Assert.Equal(3, dto.Terms!.Count);                                                                 // the account's own term, then two renewals
        Assert.Equal((new DateOnly(2025, 6, 30), 6, new DateOnly(2025, 6, 30)), (dto.Terms[0].LastDay!.Value, dto.Terms[0].Months!.Value, dto.Terms[0].RenewedOn!.Value));
        Assert.Equal((new DateOnly(2025, 12, 31), 6, new DateOnly(2025, 12, 31)), (dto.Terms[1].LastDay!.Value, dto.Terms[1].Months!.Value, dto.Terms[1].RenewedOn!.Value));
        Assert.Equal(new DateOnly(2027, 6, 30), dto.Terms[2].LastDay);

        var account = await context.ObligationAccounts.AsNoTracking().SingleAsync();
        var quotes = await new ObligationCollectionSource(context).GetQuotesAsync(seed.TenantId, [account], new DateOnly(2026, 10, 4), default, lookbackMonths: 120);
        Assert.Equal(new DateOnly(2026, 10, 1), quotes.Max(q => q.PeriodStart));                           // the renewed term keeps it accruing
        Assert.Equal(22, quotes.Count);                                                                    // Jan 2025 .. Oct 2026, no gap
    }

    [SkippableFact]
    public async Task ContractMonths_AnExpiredMonthsTerm_IsExpiredForArrears_AndStillRunningBeforeItsLastDay()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var id = await CreateSignedAsync(seed, new(2026, 3, 1), null, 6);                                  // last day 2026-08-31
        await using var context = db.CreateContext(seed.TenantId);
        var account = await context.ObligationAccounts.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 31), SpaceArrears.TermLastDay(account, []));
        Assert.Equal(SpaceArrears.ExpiredContract, SpaceArrears.Basis(account, [], new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 1)));
        Assert.Null(SpaceArrears.Basis(account, [], new DateOnly(2026, 4, 1), new DateOnly(2026, 8, 31)));    // the last day itself is still inside the term
        Assert.Equal(id, account.Id);
    }

    [SkippableFact]
    public async Task ContractMonths_ReassignRecordsTheNewHoldersMonthsTerm()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        Guid id;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var created = await Setup(context, seed).CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null,
                new DateOnly(2026, 1, 1), 1200m, ActualOccupant: "Ana Reyes"));
            Assert.True(created.IsSuccess, created.Error);
            id = created.Value!.Id;
            Assert.True((await Setup(context, seed).CloseAccountAsync(id, new(new DateOnly(2026, 6, 30)))).IsSuccess);
        }
        await using var lifecycleContext = db.CreateContext(seed.TenantId);
        var workflow = new SpaceLifecycleWorkflow(lifecycleContext, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId), new HistoricalClock());
        var command = new LifecycleCommand(LifecycleAction.Reassign, new DateOnly(2026, 7, 1), "New holder", "Contract 2", NewHolder: "Pedro Cruz", Amount: 1500m,
            ContractName: "Pedro Cruz", Reference: "KM-9", Arrangement: OccupancyArrangement.SignedContract, TermMonths: 6);
        var preview = (await workflow.PreviewAsync(id, command)).Value!;
        Assert.True(preview.CanExecute, preview.Explanation);
        Assert.Contains("Dec 31, 2026", preview.Facts["Contract term"]);
        Assert.True((await workflow.ExecuteAsync(id, new(Guid.NewGuid(), preview.Version, command))).IsSuccess);
        var successor = await lifecycleContext.ObligationAccounts.AsNoTracking().SingleAsync(a => a.PredecessorAccountId == id);
        Assert.Equal((0, 6, new DateOnly(2026, 7, 1)), (successor.ContractYears, successor.ContractMonths, successor.ContractEffectiveDate));
    }

    [SkippableFact]
    public async Task ContractMonths_CorrectionKeepsTheTermLockedAfterAssessment_ButTwelveMonthsIsTheSameAsOneYear()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var fresh = await CreateSignedAsync(seed, new(2026, 2, 1), null, 3);
        var yearly = await CreateSignedAsync(seed, new(2026, 2, 1), 1, null);
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var account = await context.ObligationAccounts.SingleAsync(a => a.Id == yearly);
            context.ObligationPeriods.Add(ObligationPeriod.Assess(account, await context.ObligationRates.FirstAsync(r => r.ObligationAccountId == yearly), new DateOnly(2026, 2, 1), "test"));
            await context.SaveChangesAsync();
        }
        async Task<Result<bool>> Correct(Guid id, int? years, int? months)
        {
            await using var context = db.CreateContext(seed.TenantId);
            var account = await context.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == id);
            return await Setup(context, seed).CorrectHolderAsync(id, new(account.ActualOccupant!, OccupancyArrangement.SignedContract, account.ContractReference,
                account.ContractName, account.ContractEffectiveDate, years, null, null, null, ContractMonths: months));
        }
        Assert.True((await Correct(fresh, null, 9)).IsSuccess);                                          // no assessed history: free to correct
        Assert.Equal((0, 9), ((await ReadAsync(seed, fresh)).ContractYears, (await ReadAsync(seed, fresh)).ContractMonths));
        Assert.False((await Correct(yearly, null, 6)).IsSuccess);                                        // assessed: a different term is refused
        Assert.True((await Correct(yearly, null, 12)).IsSuccess);                                        // ... but 12 months is the recorded one year
        var kept = await ReadAsync(seed, yearly);
        Assert.Equal((1, (int?)null), (kept.ContractYears, kept.ContractMonths));
        Assert.False((await Correct(fresh, null, 0)).IsSuccess);                                         // a zero-length term is never recorded
    }

    [SkippableFact]
    public async Task ContractMonths_ImportAcceptsAnOptionalMonthsColumn_AndLegacyRowsAreUnchanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        ImportSpaceHolderRow Row(int? years, int? months, string occupant) => new(SignedKm(seed, new(2026, 2, 1), years, months) with { ActualOccupant = occupant });
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var result = await Setup(context, seed).ImportSpaceHoldersAsync(new([Row(3, null, "Legacy yearly"), Row(null, 6, "Months only"), Row(1, 6, "Years and months"),
                new(SignedKm(seed, new(2026, 2, 1), null, null) with { ActualOccupant = "No duration", ContractEffectiveDate = null, ContractName = null })]));
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(4, result.Value!.Imported);
        }
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var byHolder = (await context.ObligationAccounts.AsNoTracking().ToListAsync()).ToDictionary(a => a.ActualOccupant!);
            Assert.Equal((3, (int?)null), (byHolder["Legacy yearly"].ContractYears, byHolder["Legacy yearly"].ContractMonths));
            Assert.Equal((0, (int?)6), (byHolder["Months only"].ContractYears, byHolder["Months only"].ContractMonths));
            Assert.Equal((1, (int?)6), (byHolder["Years and months"].ContractYears, byHolder["Years and months"].ContractMonths));
            Assert.Equal((null, null), (byHolder["No duration"].ContractYears, byHolder["No duration"].ContractMonths));
        }
        // A bad months value reviews the row and saves nothing.
        await using var bad = db.CreateContext(seed.TenantId);
        var refused = await Setup(bad, seed).ImportSpaceHoldersAsync(new([Row(null, 0, "Zero term")]));
        Assert.True(refused.IsSuccess);
        Assert.Equal(0, refused.Value!.Imported);
        Assert.Single(refused.Value.NeedsReview);
        Assert.Equal(4, await bad.ObligationAccounts.CountAsync());
    }

    [SkippableFact]
    public async Task ContractMonths_AnotherTenantCannotReadOrCorrectOrRenewAMonthsContract()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var owner = await SeedAsync();
        var foreign = await SeedAsync();
        var id = await CreateSignedAsync(owner, new(2025, 1, 1), null, 6);
        await using var context = db.CreateContext(foreign.TenantId);
        var workflow = Setup(context, foreign);
        Assert.DoesNotContain(id, (await workflow.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Select(a => a.Id));
        var correction = await workflow.CorrectHolderAsync(id, new("X", OccupancyArrangement.SignedContract, "R", "N", new DateOnly(2025, 1, 1), null, null, null, null, ContractMonths: 9));
        Assert.Equal(ResultStatus.NotFound, correction.Status);
        var lifecycle = new SpaceLifecycleWorkflow(context, new TestActor(foreign.UserId, foreign.TenantId, "SuperAdmin"), new FixedTenant(foreign.TenantId), new HistoricalClock());
        var renew = await lifecycle.PreviewAsync(id, new(LifecycleAction.Renew, new DateOnly(2025, 7, 1), "x", "y", TermMonths: 6));
        Assert.False(renew.IsSuccess && renew.Value!.CanExecute);
        await using var check = db.CreateContext(owner.TenantId);
        Assert.Equal(6, (await check.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == id)).ContractMonths);
        Assert.Empty(await check.SpaceContractTerms.ToListAsync());
    }
}
