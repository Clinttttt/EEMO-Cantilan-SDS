using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class FineAssessmentTests
{
    [SkippableFact]
    public async Task Mobile_rent_and_assessed_fine_share_a_visit_but_keep_distinct_src_and_classifications()
    {
        var (seed, fine) = await SettlementSeedAsync();
        await using var db = database.CreateContext(seed.TenantId);
        var rentClassification = RevenueClassification.Create(
            ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), seed.TenantId);
        db.RevenueClassifications.Add(rentClassification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
            rentClassification.Id, new DateOnly(2023, 1, 1), "Kanmanggay rent",
            RevenueInstrumentType.OfficialReceipt, seed.TenantId));
        db.ObligationRates.Add(ObligationRate.Create(
            seed.TenantId, seed.Account.AccountId, new DateOnly(Today.Year, Today.Month, 1), 175m, "head"));
        await db.SaveChangesAsync();

        var (_, flow) = await CollectorFineSessionAsync(db, seed, assigned: true);
        var source = FineSpaceIdentity(seed);
        var options = (await flow.DiscoverNativeAsync(source)).Value!.Operations;
        var fineOption = Assert.Single(Assert.Single(options, x => x.Kind == CollectionSessionItemKind.FineAssessment).Choices!);
        var rentalOption = Assert.Single(options.Where(x => x.Kind == CollectionSessionItemKind.Obligation)
            .SelectMany(x => x.Choices ?? []), x => x.Identity.Year == Today.Year && x.Identity.Month == Today.Month);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, rentalOption.Instrument);
        Assert.True(rentalOption.ServerAmount > 0m);

        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, rentalOption.ServerAmount!.Value,
                Obligation: new(seed.Account.AccountId, Today.Year, Today.Month)),
             new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, fineOption.ServerAmount!.Value,
                Fine: new(fine, fineOption.Identity.FineVersion))], SourceIdentity: source);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(x => x.Message)));
        Assert.Equal(rentalOption.ServerAmount + fineOption.ServerAmount, quote.GrandTotal);
        Assert.All(quote.Items, x => Assert.Equal(RevenueInstrumentType.OfficialReceipt, x.Instrument));

        var posted = (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, posted.Status);
        Assert.Equal(2, posted.Collections.Count);
        Assert.Equal(2, posted.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(posted.Collections, c => Assert.StartsWith("SRC-", c.ReferenceCode));
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Collections.CountAsync());
        var codes = await (from line in db.CollectionLines.AsNoTracking()
                           join classification in db.RevenueClassifications.AsNoTracking()
                               on line.RevenueClassificationId equals classification.Id
                           select classification.SemanticCode).ToArrayAsync();
        Assert.Contains(EEMOCantilanSDS.Domain.Constants.RevenueClassificationCodes.PenaltiesAndFines, codes);
        Assert.Contains(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), codes);
        Assert.Single(await db.CollectionAllocations.Where(x => x.SourceKind == CollectionSourceKind.FineAssessment).ToListAsync());
        Assert.Single(await db.MobileCollectionSessions.ToListAsync()); // one atomic checkout, not a new ledger
    }
}
