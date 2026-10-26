using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-19: historical stall-rent arrears reach the Collector through the existing canonical collection session, against the ORIGINAL contract and
/// period. These tests share the New Collection harness so "New Collection already covers it" is proved, not assumed.
/// </summary>
public sealed partial class CollectionSessionTests
{
    private sealed record RentWorld(Guid FacilityId, Guid StallId, Guid FormerContractId, Guid CurrentContractId);

    /// <summary>
    /// An NCC (monthly-rent) stall the collector is assigned to. The former lessee held it Jan-Jun 2026 and, when <paramref name="relet"/>, a successor
    /// from 1 Jul 2026. Canonical PaymentRecords exist only for the months listed (a month nobody touched has no record, as in production).
    /// </summary>
    private Task<RentWorld> SeedRentStallAsync(AppDbContext db, World w, bool relet, params (int Year, int Month, decimal Paid)[] records)
        => SeedRentStallAsync(db, w, relet, FacilityCode.NCC, "R-1", records);

    private async Task<RentWorld> SeedRentStallAsync(AppDbContext db, World w, bool relet, FacilityCode code, string stallNo,
        params (int Year, int Month, decimal Paid)[] records)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(x => x.Code == code);
        if (facility is null)
        {
            facility = Facility.Create(code, code.ToString(), code.ToString(), municipalityId: w.TenantId);
            var classification = RevenueClassification.Create(RevenueClassificationCodes.ForMonthlyRental(code), w.TenantId);
            db.AddRange(facility, classification, RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1), "Stall Rent", RevenueInstrumentType.OfficialReceipt, w.TenantId),
                CollectorFacilityAssignment.Create(w.CollectorId, facility.Id, code));
        }
        var stall = Stall.Create(facility.Id, stallNo, 1_000m, ApplicableFees.BaseRental, null, createdBy: "test", municipalityId: w.TenantId);
        var former = Contract.Create(stall.Id, "Former Lessee", "Former Lessee", new DateOnly(2026, 1, 1), 5, 1_000m, createdBy: "test");
        var current = relet ? Contract.Create(stall.Id, "Successor Lessee", "Successor Lessee", new DateOnly(2026, 7, 1), 5, 1_000m, createdBy: "test") : null;
        db.AddRange(stall, former);
        if (current is not null) { former.Terminate("test", new DateOnly(2026, 6, 30)); db.Add(current); }
        await db.SaveChangesAsync();
        foreach (var (year, month, paid) in records)
        {
            var record = PaymentRecord.Create(stall.Id, year, month, 1_000m, "test");
            if (paid > 0m) record.UpdateStatus(PaymentStatus.Partial, paid, updatedBy: "test");
            record.MarkSettlementPendingCutover();
            var at = DateTime.UtcNow.AddMinutes(-3);
            var cutover = CollectionSettlementCutover.Freeze(w.TenantId, CollectionSourceKind.PaymentRecord, record.Id, null, record.SettlementVersion, at,
                record.BaseRentalAmount, paid, record.BaseRentalAmount - paid, "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}",
                Guid.NewGuid(), at.AddMinutes(1));
            record.ActivateCanonicalSettlement(cutover);
            db.AddRange(record, cutover);
        }
        await db.SaveChangesAsync();
        return new(facility.Id, stall.Id, former.Id, (current ?? former).Id);
    }

    private CollectionSessionSources RentSources(AppDbContext db, World w) =>
        new(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), new DiscoverySender(db, w), new NoMarketDays());

    private static IReadOnlyList<(int Year, int Month, decimal Amount)> RentChoices(CollectionSessionDiscovery d) =>
        d.Operations.Where(o => o.Kind == CollectionSessionItemKind.MonthlyRent).SelectMany(o => o.Choices ?? [])
            .Select(c => (c.Identity.Year!.Value, c.Identity.Month!.Value, c.ServerAmount ?? 0m)).OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToArray();

    private static CollectionSessionIntent RentIntent(CollectionSessionDiscovery d, CollectionSourceIdentity identity, int year, int month, decimal amount)
    {
        var choice = d.Operations.Where(o => o.Kind == CollectionSessionItemKind.MonthlyRent).SelectMany(o => o.Choices ?? [])
            .Single(c => c.Identity.Year == year && c.Identity.Month == month);
        return new CollectionSessionIntent(Guid.NewGuid(), Today, null, [new(Guid.NewGuid(), CollectionSessionItemKind.MonthlyRent, amount,
            Rent: new(choice.Identity.StallId!.Value, year, month, choice.Identity.SourceVersion))]) { SourceIdentity = identity };
    }

    [SkippableFact]
    public async Task NewCollection_covers_every_canonical_month_of_the_same_contract_and_a_successor_never_sees_a_former_lessees_months()
    {
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        // Former lessee Jan-Jun (Jan, Mar recorded; Feb never touched => no record => stays on its legacy path). Successor from Jul (Aug, Oct recorded).
        var rent = await SeedRentStallAsync(db, w, relet: true, (2026, 1, 0m), (2026, 3, 0m), (2026, 8, 0m), (2026, 10, 0m));
        var sources = RentSources(db, w);

        var current = RentChoices(await sources.DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, rent.CurrentContractId), Today, default));
        Assert.Equal([(2026, 8, 1_000m), (2026, 10, 1_000m)], current);                    // a prior month of the SAME contract is already in New Collection

        var former = RentChoices(await sources.DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, rent.FormerContractId), Today, default));
        Assert.Equal([(2026, 1, 1_000m), (2026, 3, 1_000m)], former);                     // the former lessee's own months, and only those
    }

    [SkippableFact]
    public async Task A_former_contracts_arrears_post_through_the_canonical_session_once_at_their_own_balance_and_never_under_the_successor()
    {
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        var rent = await SeedRentStallAsync(db, w, relet: true, (2026, 1, 0m), (2026, 3, 0m), (2026, 8, 0m));
        var sources = RentSources(db, w);
        var flow = Workflow(db, w, sources);
        var formerId = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, rent.FormerContractId);
        var currentId = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, rent.CurrentContractId);
        var discovery = await sources.DiscoverNativeAsync(formerId, Today, default);

        // Partial: 400 of January. The rest stays owed, a retry posts nothing more, and more than the balance is refused.
        var partial = RentIntent(discovery, formerId, 2026, 1, 400m);
        var quote = (await flow.QuoteAsync(partial)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(p => p.Message)));
        var first = (await flow.RecordAsync(new(partial, quote.QuoteFingerprint))).Value!;
        var replay = (await flow.RecordAsync(new(partial, quote.QuoteFingerprint))).Value!;
        Assert.Equal(first.Collections.Single().ReferenceCode, replay.Collections.Single().ReferenceCode);
        Assert.Equal(1, await db.Collections.CountAsync());
        Assert.Equal([(2026, 1, 600m), (2026, 3, 1_000m)], RentChoices(await sources.DiscoverNativeAsync(formerId, Today, default)));
        var over = (await flow.QuoteAsync(RentIntent(await sources.DiscoverNativeAsync(formerId, Today, default), formerId, 2026, 1, 700m))).Value!;
        Assert.False(over.CanRecord);

        // The successor's session cannot take the former lessee's month, even with the exact period and source version.
        var wrongOwner = RentIntent(discovery, formerId, 2026, 3, 1_000m) with { SourceIdentity = currentId };
        Assert.False((await flow.QuoteAsync(wrongOwner)).Value!.CanRecord);

        // A collector not assigned to the facility is offered nothing for the former contract.
        db.RemoveRange(await db.CollectorFacilityAssignments.Where(x => x.CollectorId == w.CollectorId && x.FacilityCode == FacilityCode.NCC).ToListAsync());
        await db.SaveChangesAsync();
        Assert.Empty(RentChoices(await sources.DiscoverNativeAsync(formerId, Today, default)));
    }
}
