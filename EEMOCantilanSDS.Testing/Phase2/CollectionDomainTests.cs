using System.Reflection;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class CollectionDomainTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 23);

    private static (RevenueClassification Classification, RevenueClassificationPolicy Policy) Reference(
        Guid? municipalityId = null,
        string semanticCode = "MARKET_FEES",
        DateOnly? effectiveDate = null)
    {
        var tenant = municipalityId ?? Guid.NewGuid();
        var classification = RevenueClassification.Create(semanticCode, tenant);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id,
            effectiveDate ?? BusinessDate,
            "Market Fees",
            RevenueInstrumentType.CashTicket,
            tenant);
        return (classification, policy);
    }

    private static Collection Post(
        IEnumerable<CollectionLineDraft>? lines = null,
        Guid? collectorId = null,
        Guid? payorUserId = null,
        string? payerName = null,
        Guid? clientOperationId = null) => Collection.Post(
            BusinessDate,
            DateTime.SpecifyKind(new DateTime(2026, 9, 23, 8, 30, 0), DateTimeKind.Utc),
            "actor-17",
            "A. Recorder",
            "SuperAdmin",
            lines ?? [Draft(75m)],
            collectorId,
            payorUserId,
            payerName,
            clientOperationId);

    private static CollectionLineDraft Draft(
        decimal amount,
        RevenueClassification? classification = null,
        RevenueClassificationPolicy? policy = null,
        CollectionSourceKind? sourceKind = null,
        Guid? sourceId = null,
        CollectionSourcePart? sourcePart = null)
    {
        if (classification is null || policy is null)
        {
            var refs = Reference();
            classification ??= refs.Classification;
            policy ??= refs.Policy;
        }

        return new CollectionLineDraft(classification, policy, amount, sourceKind, sourceId, sourcePart);
    }

    [Fact]
    public void CompleteAggregateComputesTotalAndAllowsAnonymousOptionalAttribution()
    {
        var refs = Reference();
        var collection = Post([
            Draft(40m, refs.Classification, refs.Policy),
            Draft(12.35m, refs.Classification, refs.Policy)]);

        Assert.Equal(52.35m, collection.TotalAmount);
        Assert.Equal(2, collection.Lines.Count);
        Assert.All(collection.Lines, line => Assert.Equal(0, line.Amount % 0.01m));
        Assert.Null(collection.CollectorId);
        Assert.Null(collection.PayorUserId);
        Assert.Null(collection.PayerName);
        Assert.Null(collection.ClientOperationId);
        Assert.NotEqual(Guid.Empty, collection.MunicipalityId);
        Assert.Equal(DateTimeKind.Utc, collection.RecordedAtUtc.Kind);
    }

    [Fact]
    public void CollectionRequiresAtLeastOneLine()
    {
        Assert.Throws<ArgumentException>(() => Post([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void CollectionRejectsNonPositiveLineAmounts(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Post([Draft(amount)]));
    }

    [Fact]
    public void CollectionRejectsAmountsWithMoreThanTwoDecimalPlacesWithoutRounding()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Post([Draft(1.001m)]));
    }

    [Fact]
    public void CollectionRequiresActorSnapshotAndUtcRecordedTime()
    {
        var refs = Reference();
        Assert.Throws<ArgumentException>(() => Collection.Post(
            BusinessDate, DateTime.UtcNow, " ", "Recorder", "SuperAdmin", [new(refs.Classification, refs.Policy, 1m)]));
        Assert.Throws<ArgumentException>(() => Collection.Post(
            BusinessDate, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local), "actor", "Recorder", "SuperAdmin",
            [new(refs.Classification, refs.Policy, 1m)]));
    }

    [Fact]
    public void CollectorPayerAndOperationIdentityAreOptionalButValidIdsAreRetained()
    {
        var collector = Guid.NewGuid();
        var payor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var collection = Post(collectorId: collector, payorUserId: payor, payerName: "Anonymous walk-in", clientOperationId: operation);

        Assert.Equal(collector, collection.CollectorId);
        Assert.Equal(payor, collection.PayorUserId);
        Assert.Equal("Anonymous walk-in", collection.PayerName);
        Assert.Equal(operation, collection.ClientOperationId);
        Assert.Throws<ArgumentException>(() => Post(collectorId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Post(payorUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Post(clientOperationId: Guid.Empty));
    }

    [Fact]
    public void PolicyMustBelongToLineClassificationAndBeEffectiveOnBusinessDate()
    {
        var first = Reference();
        var other = Reference(semanticCode: "OTHER_SOURCE");

        Assert.Throws<ArgumentException>(() => Post([Draft(10m, first.Classification, other.Policy)]));
        var futurePolicy = RevenueClassificationPolicy.Create(
            first.Classification.Id, BusinessDate.AddDays(1), "Future", RevenueInstrumentType.CashTicket,
            first.Classification.MunicipalityId);
        Assert.Throws<ArgumentException>(() => Post([Draft(10m, first.Classification, futurePolicy)]));

        var todayPolicy = RevenueClassificationPolicy.Create(
            first.Classification.Id, BusinessDate, "Today", RevenueInstrumentType.CashTicket,
            first.Classification.MunicipalityId);
        Assert.Equal(10m, Post([Draft(10m, first.Classification, todayPolicy)]).TotalAmount);
    }

    [Fact]
    public void AllLinesMustShareOneMunicipality()
    {
        Assert.Throws<ArgumentException>(() => Post([Draft(10m), Draft(5m)]));
    }

    [Theory]
    [InlineData(CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity)]
    [InlineData(CollectionSourceKind.UtilityBill, CollectionSourcePart.Water)]
    [InlineData(CollectionSourceKind.DailyCollection, CollectionSourcePart.DailyFee)]
    [InlineData(CollectionSourceKind.DailyCollection, CollectionSourcePart.FishFee)]
    public void ComponentSourcesRequireTheCorrectPart(CollectionSourceKind kind, CollectionSourcePart part)
    {
        var line = Draft(10m, sourceKind: kind, sourceId: Guid.NewGuid(), sourcePart: part);
        Assert.Single(Post([line]).Lines);
    }

    [Fact]
    public void SourceIdentityMustBeStructurallyConsistent()
    {
        Assert.Throws<ArgumentException>(() => Post([Draft(10m, sourceId: Guid.NewGuid())]));
        Assert.Throws<ArgumentException>(() => Post([Draft(10m, sourceKind: CollectionSourceKind.PaymentRecord)]));
        Assert.Throws<ArgumentException>(() => Post([Draft(10m,
            sourceKind: CollectionSourceKind.UtilityBill, sourceId: Guid.NewGuid())]));
        Assert.Throws<ArgumentException>(() => Post([Draft(10m,
            sourceKind: CollectionSourceKind.PaymentRecord, sourceId: Guid.NewGuid(), sourcePart: CollectionSourcePart.Water)]));
        Assert.Single(Post([Draft(10m, sourceKind: CollectionSourceKind.PaymentRecord, sourceId: Guid.NewGuid())]).Lines);
    }

    [Fact]
    public void AggregateAndLinesExposeNoPublicMutationOrDeletionApi()
    {
        AssertNoPublicSetters(typeof(Collection));
        AssertNoPublicSetters(typeof(CollectionLine));

        var publicInstanceMethods = typeof(Collection).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);
        Assert.Empty(publicInstanceMethods);

        var lineMethods = typeof(CollectionLine).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);
        Assert.Empty(lineMethods);
    }

    private static void AssertNoPublicSetters(Type entityType)
    {
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Assert.Null(property.GetSetMethod());
    }
}
