using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Verified_vendor_occupancy_inherits_exact_assessed_fine_but_same_name_never_does()
    {
        var today = PhilippineTime.Today;
        var w = await SeedAsync(section: MarketSection.MeatSection);
        await using var db = database.CreateContext(w.TenantId);
        await EnableOffice(db, w);
        var office = Office(db, w, "SuperAdmin");
        var candidate = Assert.Single((await office.NpmVendorCandidatesAsync(today.Year)).Value!.Rows);
        var operation = Guid.NewGuid();
        var linked = await office.RegisterAsync(new(operation, today.Year, FishMeatVendorType.Meat,
            VendorRegistrationKind.New, "Verified Meat Vendor", NpmAssociation: Confirmation(candidate, operation)));
        Assert.True(linked.IsSuccess, linked.Error);
        var byName = await office.RegisterAsync(new(Guid.NewGuid(), today.Year, FishMeatVendorType.Meat,
            VendorRegistrationKind.New, candidate.Occupant)); // no office-confirmed NPM association
        Assert.True(byName.IsSuccess, byName.Error);

        var actor = new Caller(w.CollectorId, w.TenantId, "SuperAdmin");
        var clock = new Clock(today);
        var definition = PenaltyDefinition.Create(w.TenantId, "NPM_VERIFIED_FINE", today.AddYears(-1),
            "Verified NPM incident", null, GovernedServiceBasis.DirectApprovedAmount,
            null, 200m, true, "head");
        var policy = FineAssessmentPolicy.Create(w.TenantId, definition.Id,
            FineAccountKind.FacilityContract, FacilityCode.NPM, "Approved source policy",
            FinePenaltyDateBasis.AssessmentDate, false, false, w.CollectorId, clock.UtcNow);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, w.TenantId);
        db.AddRange(definition, policy, classification,
            RevenueClassificationPolicy.Create(classification.Id, today.AddYears(-1), "Fines",
                RevenueInstrumentType.OfficialReceipt, w.TenantId));
        await db.SaveChangesAsync();

        var assessing = new FineAssessmentWorkflow(db, actor, new Tenant(w.TenantId), clock);
        var created = await assessing.CreateAsync(new(Guid.NewGuid(),
            new(FineAccountKind.FacilityContract, candidate.ContractId), policy.Id,
            today.AddDays(-1), "NPM-VERIFIED-001", "Verified incident", 125m));
        Assert.True(created.IsSuccess, created.Error);
        var fineId = created.Value!.Id;

        var collector = new Caller(w.CollectorId, w.TenantId);
        var sources = new CollectionSessionSources(db, collector, new Tenant(w.TenantId), clock,
            null!, new NoMarketDays());
        var workflow = NativeWorkflow(db, w, sources);
        var linkedIdentity = new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration,
            linked.Value!.Id);
        var otherIdentity = new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration,
            byName.Value!.Id);
        var linkedDiscovery = (await workflow.DiscoverNativeAsync(linkedIdentity)).Value!;
        var choice = Assert.Single(Assert.Single(linkedDiscovery.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment).Choices!);
        Assert.Equal(fineId, choice.Identity.FineAssessmentId);
        Assert.Equal(125m, choice.ServerAmount);
        Assert.Equal(CollectionSessionAmountRule.PreparedBalance, choice.AmountRule);
        Assert.True(choice.CanEnterAmount);
        Assert.Equal(125m, choice.MaximumAmount);
        Assert.DoesNotContain((await workflow.DiscoverNativeAsync(otherIdentity)).Value!.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment);

        var item = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment,
            45m, Fine: new(fineId, choice.Identity.FineVersion));
        var intent = new CollectionSessionIntent(Guid.NewGuid(), today, null, [item],
            SourceIdentity: linkedIdentity);
        Assert.False((await workflow.QuoteAsync(intent with {
            ClientCollectionSessionId = Guid.NewGuid(), SourceIdentity = otherIdentity
        })).Value!.CanRecord); // never resolve liability by vendor display name

        var quote = (await workflow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(p => p.Message)));
        Assert.Equal(45m, Assert.Single(quote.Items).Amount);
        var posted = (await workflow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, posted.Status);
        Assert.Equal(45m, Assert.Single(posted.Collections).Amount);
        Assert.StartsWith("SRC-", posted.Collections[0].ReferenceCode);
        var replay = (await workflow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.True(replay.ExistingOutcome);
        Assert.Equal(posted.Collections[0].CollectionId, Assert.Single(replay.Collections).CollectionId);

        db.ChangeTracker.Clear();
        var assessmentQuote = (await new CollectionComposerWorkflow(db, collector,
            new Tenant(w.TenantId), clock).QuoteFineAsync(fineId)).Value!;
        Assert.Equal(80m, assessmentQuote.Remaining);
        Assert.Equal("Partial", assessmentQuote.Status);
        var updatedDiscovery = (await workflow.DiscoverNativeAsync(linkedIdentity)).Value!;
        var remainingChoice = Assert.Single(Assert.Single(updatedDiscovery.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment).Choices!);
        Assert.Equal(80m, remainingChoice.ServerAmount);
        Assert.Equal(assessmentQuote.Version, remainingChoice.Identity.FineVersion);

        // The original quote cannot spend the updated balance; the server never silently reduces an old intent.
        var stale = (await workflow.QuoteAsync(intent with { ClientCollectionSessionId = Guid.NewGuid() })).Value!;
        Assert.Contains(stale.Problems, p => p.Code == "BalanceChanged");
        foreach (var invalid in new[] { 0m, -1m, 80.01m, 5.001m })
        {
            var attempt = new CollectionSessionIntent(Guid.NewGuid(), today, null,
                [new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, invalid,
                    Fine: new(fineId, assessmentQuote.Version))], SourceIdentity: linkedIdentity);
            var rejected = (await workflow.QuoteAsync(attempt)).Value!;
            Assert.False(rejected.CanRecord);
            Assert.Contains(rejected.Problems, p => p.Code is "InvalidIntent" or "InvalidAmount");
        }

        var finalIntent = new CollectionSessionIntent(Guid.NewGuid(), today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, 80m,
                Fine: new(fineId, assessmentQuote.Version))], SourceIdentity: linkedIdentity);
        var finalQuote = (await workflow.QuoteAsync(finalIntent)).Value!;
        Assert.True(finalQuote.CanRecord, string.Join("; ", finalQuote.Problems.Select(p => p.Message)));
        Assert.Equal(80m, Assert.Single(finalQuote.Items).Amount);
        var finalPosted = (await workflow.RecordAsync(new(finalIntent, finalQuote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, finalPosted.Status);
        Assert.Equal(80m, Assert.Single(finalPosted.Collections).Amount);
        Assert.NotEqual(posted.Collections[0].CollectionId, finalPosted.Collections[0].CollectionId);
        db.ChangeTracker.Clear();
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.SourceKind == CollectionSourceKind.FineAssessment).ToListAsync();
        Assert.Equal(new[] { 45m, 80m }, allocations.Select(x => x.Amount).OrderBy(x => x).ToArray());
        Assert.All(allocations, x => Assert.Equal(fineId, x.FineAssessmentId));
        var fullySettled = (await new CollectionComposerWorkflow(db, collector,
            new Tenant(w.TenantId), clock).QuoteFineAsync(fineId)).Value!;
        Assert.Equal(125m, fullySettled.Settled);
        Assert.Equal(0m, fullySettled.Remaining);
        Assert.False(fullySettled.CanSettle);
        Assert.Equal(2, await db.Collections.CountAsync());
    }
}
