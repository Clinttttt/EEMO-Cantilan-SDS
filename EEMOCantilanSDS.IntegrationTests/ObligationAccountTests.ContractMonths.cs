using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Space Rental contract duration is recorded in WHOLE YEARS. A duration in months can no longer be entered (create, import, correction, renew, reassign),
/// and a holder who pays monthly without a signed contract is recorded as "No contract (space only)". A term that was already stored in months is never
/// converted, deleted or invalidated: it is still read, still expires on its month-based last day (ST-08) and is untouched by an unrelated edit.
/// </summary>
public sealed partial class ObligationAccountTests
{
    private static CreateObligationAccountRequest SignedKm(Seed seed, DateOnly start, int? years, int? months, DateOnly? contractDate = null,
        ObligationKind kind = ObligationKind.KanmanggaySpaceRental) =>
        new(kind, seed.PayorId, null, "", null, null, start, 1200m, OccupancyArrangement.SignedContract, "REF-" + Guid.NewGuid().ToString("N")[..6],
            "Occupant " + Guid.NewGuid().ToString("N")[..6], "Lessee", contractDate ?? start, years, ContractMonths: months);

    private async Task<Guid> CreateSignedAsync(Seed seed, DateOnly start, int? years, DateOnly? contractDate = null,
        ObligationKind kind = ObligationKind.KanmanggaySpaceRental)
    {
        await using var context = db.CreateContext(seed.TenantId);
        var created = await Setup(context, seed).CreateAccountAsync(SignedKm(seed, start, years, null, contractDate, kind));
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!.Id;
    }

    /// <summary>An account whose term is ALREADY stored in months, as one recorded before months were retired would be (the entity still reads it).</summary>
    private async Task<Guid> SeedStoredMonthsAsync(Seed seed, DateOnly start, int months, DateOnly? contractDate = null)
    {
        var id = await CreateSignedAsync(seed, start, 1, contractDate);
        await using var context = db.CreateContext(seed.TenantId);
        var account = await context.ObligationAccounts.SingleAsync(a => a.Id == id);
        account.SetLeaseDetails(account.ContractName, contractDate ?? start, months / 12, null, null, null, months % 12 == 0 ? null : months);
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<ObligationAccountDto> ReadAsync(Seed seed, Guid id, ObligationKind kind = ObligationKind.KanmanggaySpaceRental)
    {
        await using var context = db.CreateContext(seed.TenantId);
        return (await Setup(context, seed).GetAccountsAsync(kind)).Value!.Single(a => a.Id == id);
    }

    [SkippableFact]
    public async Task WholeYears_AreRecordedAsBefore_ForEveryPresetAndACustomNumber_AndANoContractHolderNeedsNoTerm()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        foreach (var years in new[] { 1, 2, 3, 5, 7 })
        {
            var id = await CreateSignedAsync(seed, new(2026, 2, 1), years);
            var dto = await ReadAsync(seed, id);
            Assert.Equal((years, (int?)null), (dto.ContractYears, dto.ContractMonths));
        }
        await using var context = db.CreateContext(seed.TenantId);
        var spaceOnly = await Setup(context, seed).CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, new(2026, 2, 1), 1200m,
            OccupancyArrangement.SpaceOnly, null, "Month-to-month holder", null, null, null));
        Assert.True(spaceOnly.IsSuccess, spaceOnly.Error);
        Assert.Equal((null, null), (spaceOnly.Value!.ContractYears, spaceOnly.Value.ContractMonths));
    }

    [SkippableFact]
    public async Task AContractInMonths_IsRefusedEverywhere_AndNothingIsSaved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var yearly = await CreateSignedAsync(seed, new(2026, 2, 1), 3);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Setup(context, seed);

        var create = await workflow.CreateAccountAsync(SignedKm(seed, new(2026, 3, 1), null, 6));
        Assert.False(create.IsSuccess); Assert.Equal(ResultStatus.Invalid, create.Status); Assert.Contains("whole years", create.Error);

        var import = await workflow.ImportSpaceHoldersAsync(new([new(SignedKm(seed, new(2026, 3, 1), 1, 6) with { ActualOccupant = "Months row" })]));
        Assert.True(import.IsSuccess); Assert.Equal(0, import.Value!.Imported); Assert.Single(import.Value.NeedsReview);

        var correction = await workflow.CorrectHolderAsync(yearly, new("X", OccupancyArrangement.SignedContract, "R", "N", new DateOnly(2026, 2, 1), 3, null, null, null, ContractMonths: 6));
        Assert.False(correction.IsSuccess); Assert.Equal(ResultStatus.Invalid, correction.Status);

        var lifecycle = new SpaceLifecycleWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId), new HistoricalClock());
        var renew = await lifecycle.PreviewAsync(yearly, new(LifecycleAction.Renew, new DateOnly(2029, 2, 1), "Renewal", "Res. 1", TermYears: 1, TermMonths: 6));
        Assert.False(renew.IsSuccess && renew.Value!.CanExecute);

        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.ObligationAccounts.AsNoTracking().ToListAsync());
        Assert.Null((await verify.ObligationAccounts.AsNoTracking().SingleAsync()).ContractMonths);
        Assert.Empty(await verify.SpaceContractTerms.ToListAsync());
    }

    [SkippableFact]
    public async Task AStoredMonthsTerm_StillExpiresOnItsMonthBasedLastDay_AndIsNeverConverted()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var sixMonths = await SeedStoredMonthsAsync(seed, new(2026, 1, 1), 6);                       // last day 2026-06-30
        var midMonth = await SeedStoredMonthsAsync(seed, new(2026, 2, 1), 6, new(2026, 2, 15));      // last day 2026-08-14: August is still inside
        var oneYear = await CreateSignedAsync(seed, new(2025, 11, 1), 1);                            // yearly: unchanged (last day 2026-10-31)
        await using var read = db.CreateContext(seed.TenantId);
        var accounts = await read.ObligationAccounts.AsNoTracking().ToListAsync();
        var quotes = await new ObligationCollectionSource(read).GetQuotesAsync(seed.TenantId, accounts, new DateOnly(2026, 10, 4), default, lookbackMonths: 120);
        List<DateOnly> Months(Guid id) => quotes.Where(q => q.AccountId == id).Select(q => q.PeriodStart).OrderBy(x => x).ToList();

        Assert.Equal(Enumerable.Range(1, 6).Select(m => new DateOnly(2026, m, 1)), Months(sixMonths));
        Assert.Equal(Enumerable.Range(2, 7).Select(m => new DateOnly(2026, m, 1)), Months(midMonth));
        Assert.Equal(12, Months(oneYear).Count);
        var stored = accounts.Single(a => a.Id == sixMonths);
        Assert.Equal((0, 6), (stored.ContractYears, stored.ContractMonths));                         // read as recorded, not rewritten
        Assert.Equal(new DateOnly(2026, 6, 30), SpaceArrears.TermLastDay(stored, []));
        Assert.Equal(SpaceArrears.ExpiredContract, SpaceArrears.Basis(stored, [], new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1)));
    }

    [SkippableFact]
    public async Task AnUnrelatedEditOfAStoredMonthsAccount_LeavesItsTermExactlyAsRecorded()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var id = await SeedStoredMonthsAsync(seed, new(2026, 2, 1), 9);
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var account = await context.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == id);
            // The edit form no longer carries months: it sends the recorded years (or none) and changes only the recorded area.
            var edit = await Setup(context, seed).CorrectHolderAsync(id, new(account.ActualOccupant!, OccupancyArrangement.SignedContract, account.ContractReference,
                account.ContractName, account.ContractEffectiveDate, null, 12.5m, null, null));
            Assert.True(edit.IsSuccess, edit.Error);
        }
        var dto = await ReadAsync(seed, id);
        Assert.Equal((12.5m, 0, (int?)9), (dto.AreaSqm, dto.ContractYears, dto.ContractMonths));
    }

    [SkippableFact]
    public async Task AnotherTenant_CannotReadCorrectOrRenewAStoredMonthsContract()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var owner = await SeedAsync();
        var foreign = await SeedAsync();
        var id = await SeedStoredMonthsAsync(owner, new(2025, 1, 1), 6);
        await using var context = db.CreateContext(foreign.TenantId);
        var workflow = Setup(context, foreign);
        Assert.DoesNotContain(id, (await workflow.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Select(a => a.Id));
        var correction = await workflow.CorrectHolderAsync(id, new("X", OccupancyArrangement.SignedContract, "R", "N", new DateOnly(2025, 1, 1), 1, null, null, null));
        Assert.Equal(ResultStatus.NotFound, correction.Status);
        var lifecycle = new SpaceLifecycleWorkflow(context, new TestActor(foreign.UserId, foreign.TenantId, "SuperAdmin"), new FixedTenant(foreign.TenantId), new HistoricalClock());
        var renew = await lifecycle.PreviewAsync(id, new(LifecycleAction.Renew, new DateOnly(2025, 7, 1), "x", "y", TermYears: 1));
        Assert.False(renew.IsSuccess && renew.Value!.CanExecute);
        await using var check = db.CreateContext(owner.TenantId);
        Assert.Equal(6, (await check.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == id)).ContractMonths);
        Assert.Empty(await check.SpaceContractTerms.ToListAsync());
    }
}
