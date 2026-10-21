using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

// Query semantics only; PostgreSQL issuance and constraints belong to the disposable integration fixture.
public sealed class CollectionReconciliationEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_source_authority_requires_cutover_evidence_known_by_the_cutoff(bool recordedCutover)
    {
        var tenant = Guid.NewGuid(); var source = Guid.NewGuid();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenant));
        var classification = RevenueClassification.Create("RENT", tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new(2020, 1, 1), "Rent", null, tenant);
        db.AddRange(classification, policy, Collection.Post(new(2026, 10, 15), new(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc), "head", "Head", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 900, CollectionSourceKind.PaymentRecord, source, null, null,
                [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, source, 900, null)])]));
        if (recordedCutover) db.Add(CollectionSettlementCutover.Freeze(tenant, CollectionSourceKind.PaymentRecord, source, null, 1,
            new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 900, 0, 900, "Reviewed source evidence", Guid.NewGuid(),
            new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var reader = new CollectionActivityReader(db);
        Assert.Empty(await reader.GetReconciliationAsync(tenant, new(2026, 10, 1), new(2026, 10, 31), new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), false));
        var latest = await reader.GetReconciliationAsync(tenant, new(2026, 10, 1), new(2026, 10, 31), new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), false);
        Assert.Equal(recordedCutover ? 1 : 0, latest.Count);
    }

    [Fact]
    public async Task Recorded_knowledge_preserves_900_and_later_activity_is_minus_900_without_new_cash()
    {
        var tenant = Guid.NewGuid();
        var accessor = Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenant);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, accessor);
        var classification = RevenueClassification.Create("MARKET_FEES", tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new(2020, 1, 1), "Market fees", null, tenant);
        var original = Collection.Post(new(2026, 10, 15), new(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc), "head", "Head", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 900, null, null, null)]);
        db.AddRange(classification, policy, original);
        db.Entry(original).Property(x => x.ReferenceCode).CurrentValue = "SRC-2026-000900";
        var correction = CollectionCorrection.Record(tenant, original.Id, null, null, null, CollectionCorrectionType.Reversal,
            new(2026, 11, 1), new(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc), -900, "Verified reversal", "head", "Head",
            [new CollectionCorrectionLineDraft(original.Lines.Single().Id, -900, [])]);
        db.Add(correction); await db.SaveChangesAsync();
        var reader = new CollectionActivityReader(db);
        var known = Assert.Single(await reader.GetReconciliationAsync(tenant, new(2026, 10, 1), new(2026, 10, 31),
            new(2026, 10, 31, 15, 59, 59, DateTimeKind.Utc), false));
        Assert.Equal(900, known.NetAmount); Assert.Empty(known.Corrections);
        var latest = Assert.Single(await reader.GetReconciliationAsync(tenant, new(2026, 10, 1), new(2026, 10, 31),
            new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), false));
        Assert.Equal(0, latest.NetAmount); Assert.Equal(-900, latest.CorrectionEffect);
        Assert.Equal(known.ReferenceCode, latest.ReferenceCode);
        var handler = new GetCollectionReconciliationQueryHandler(reader,
            Mock.Of<ICurrentUserService>(x => x.IsAuthenticated && x.UserId == Guid.NewGuid() && x.Role == "Admin" && x.MunicipalityId == tenant),
            accessor, new FixedClock(new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        var activity = (await handler.Handle(new(new(2026, 11, 1), new(2026, 11, 30), CollectionReconciliationBasis.CorrectionActivity), default)).Value!;
        Assert.Equal(0, activity.OriginalAmount); Assert.Equal(-900, activity.NetAmount);
        Assert.Equal(original.BusinessDate, Assert.Single(activity.Events).BusinessDate);
        Assert.Empty(await reader.GetReconciliationAsync(tenant, new(2026, 11, 1), new(2026, 11, 30),
            new(2026, 11, 1, 15, 59, 59, DateTimeKind.Utc), true));
        Assert.Empty(await reader.GetReconciliationAsync(Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 31), DateTime.UtcNow.AddYears(1), false));
        Assert.Equal(900, (await db.Collections.SingleAsync()).TotalAmount);
        Assert.Empty(await db.CollectionRemittances.ToArrayAsync());
    }
}
