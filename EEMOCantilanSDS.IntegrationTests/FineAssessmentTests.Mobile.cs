using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class FineAssessmentTests
{
    private sealed class FineNoMarketDays : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default)
            => throw new InvalidOperationException("Fine assessment is not a Tabo operation.");
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default)
            => throw new InvalidOperationException("Fine assessment is not a Tabo operation.");
    }

    private async Task<(Guid Collector, CollectionSessionWorkflow Flow)> CollectorFineSessionAsync(
        EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext db, Seed seed, bool assigned)
    {
        var collector = CollectorUser.Create("Fine collector", "F-1", "fine-session-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new EEMOCantilanSDS.Domain.Common.HashedPassword("test"), seed.TenantId);
        db.CollectorUsers.Add(collector);
        if (assigned)
            db.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                seed.TenantId, collector.Id, CollectorOperationCodes.KanmanggaySpaceRental, "head"));
        await db.SaveChangesAsync();
        var caller = new Actor(seed.TenantId, "Collector", collector.Id);
        var tenant = new Tenant(seed.TenantId);
        var clock = new Clock();
        var sources = new CollectionSessionSources(db, caller, tenant, clock, null!, new FineNoMarketDays());
        return (collector.Id, new CollectionSessionWorkflow(
            new CollectionSessionStore(db), sources, caller, tenant, clock,
            NullLogger<CollectionSessionWorkflow>.Instance));
    }

    private static CollectionSourceIdentity FineSpaceIdentity(Seed seed) =>
        new(SourceIdentityKind.SpaceAccount, seed.Account.AccountId);

    [SkippableFact]
    public async Task Mobile_assessed_fine_posts_own_src_and_allocates_full_balance_once()
    {
        var (seed, fine) = await SettlementSeedAsync();
        await using var db = database.CreateContext(seed.TenantId);
        var (_, flow) = await CollectorFineSessionAsync(db, seed, assigned: true);
        var identity = FineSpaceIdentity(seed);
        var eligible = (await flow.DiscoverNativeAsync(identity)).Value!;
        var offer = Assert.Single(eligible.Operations, x => x.Kind == CollectionSessionItemKind.FineAssessment);
        var choice = Assert.Single(offer.Choices!);
        Assert.Equal(fine, choice.Identity.FineAssessmentId);
        Assert.Equal(100m, choice.ServerAmount);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, choice.Instrument);
        Assert.Equal(CollectionSessionAmountRule.PreparedBalance, choice.AmountRule);
        Assert.True(choice.CanEnterAmount);
        Assert.Equal(100m, choice.MaximumAmount);

        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, 100m,
                Fine: new(fine, choice.Identity.FineVersion))], SourceIdentity: identity);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(x => x.Message)));
        Assert.Equal(100m, quote.GrandTotal);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, Assert.Single(quote.Items).Instrument);

        var saved = (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, saved.Status);
        Assert.StartsWith("SRC-", Assert.Single(saved.Collections).ReferenceCode);
        Assert.Equal(saved.Collections[0].ReferenceCode,
            Assert.Single((await flow.RecordAsync(new(intent, null))).Value!.Collections).ReferenceCode);
        db.ChangeTracker.Clear();
        var allocation = Assert.Single(await db.CollectionAllocations.AsNoTracking().ToListAsync());
        Assert.Equal((CollectionSourceKind.FineAssessment, fine, fine, 100m),
            (allocation.SourceKind, allocation.SourceId, allocation.FineAssessmentId, allocation.Amount));
        Assert.Equal(RevenueClassificationCodes.PenaltiesAndFines,
            (await db.RevenueClassifications.AsNoTracking().SingleAsync(x => x.Id ==
                db.CollectionLines.AsNoTracking().Single().RevenueClassificationId)).SemanticCode);
        Assert.Equal(1, await db.Collections.CountAsync());
        Assert.Equal(0m, (await Composer(db, seed).QuoteFineAsync(fine)).Value!.Remaining);
        Assert.DoesNotContain((await flow.DiscoverNativeAsync(identity)).Value!.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment);
        Assert.False((await flow.QuoteAsync(intent with { ClientCollectionSessionId = Guid.NewGuid() })).Value!.CanRecord);
    }

    [SkippableFact]
    public async Task Mobile_fine_requires_approved_account_assignment_and_never_matches_a_name()
    {
        var (seed, fine) = await SettlementSeedAsync();
        await using var db = database.CreateContext(seed.TenantId);
        var (_, flow) = await CollectorFineSessionAsync(db, seed, assigned: false);
        var identity = FineSpaceIdentity(seed);
        Assert.DoesNotContain((await flow.DiscoverNativeAsync(identity)).Value!.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, 100m,
                Fine: new(fine, 1))], SourceIdentity: identity);
        Assert.False((await flow.QuoteAsync(intent)).Value!.CanRecord);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync());

        // Two arbitrary source identities never become equivalent because their occupant names match.
        var other = intent with { SourceIdentity = new(SourceIdentityKind.SpaceAccount, Guid.NewGuid()) };
        Assert.False((await flow.QuoteAsync(other)).Value!.CanRecord);
    }

    [SkippableFact]
    public async Task Mobile_fine_stale_offline_quote_and_partial_amount_cannot_double_collect()
    {
        var (seed, fine) = await SettlementSeedAsync();
        await using var db = database.CreateContext(seed.TenantId);
        var (_, flow) = await CollectorFineSessionAsync(db, seed, assigned: true);
        var identity = FineSpaceIdentity(seed);
        var choice = Assert.Single(Assert.Single((await flow.DiscoverNativeAsync(identity)).Value!.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment).Choices!);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, 100m,
                Fine: new(fine, choice.Identity.FineVersion))], SourceIdentity: identity);
        var reviewed = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(reviewed.CanRecord);
        var partialQuote = (await flow.QuoteAsync(intent with {
            Items = [intent.Items[0] with { ConfirmedAmount = 50m }]
        })).Value!;
        Assert.True(partialQuote.CanRecord);
        Assert.Equal(50m, Assert.Single(partialQuote.Items).Amount);
        Assert.Empty(await db.Collections.ToListAsync()); // Quoting never collects money.

        var head = Composer(db, seed);
        var draft = await FineDraftAsync(head, fine);
        Assert.True((await head.PostAsync(draft.DraftId, new(draft.Revision, Guid.NewGuid()))).IsSuccess);
        db.ChangeTracker.Clear();

        var stale = (await flow.RecordAsync(new(intent, reviewed.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.NeedsReview, stale.Status);
        Assert.Contains(stale.Problems, p => p.Code is "SourceNotAvailable" or "BalanceChanged");
        Assert.Single(await db.Collections.ToListAsync());
        Assert.Single(await db.CollectionAllocations.ToListAsync());
        Assert.Empty(await db.MobileCollectionSessions.ToListAsync());
    }
}
