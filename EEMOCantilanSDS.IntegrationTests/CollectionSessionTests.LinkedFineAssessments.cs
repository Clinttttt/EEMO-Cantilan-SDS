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
        Assert.DoesNotContain((await workflow.DiscoverNativeAsync(otherIdentity)).Value!.Operations,
            x => x.Kind == CollectionSessionItemKind.FineAssessment);

        var item = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment,
            125m, Fine: new(fineId, choice.Identity.FineVersion));
        var intent = new CollectionSessionIntent(Guid.NewGuid(), today, null, [item],
            SourceIdentity: linkedIdentity);
        Assert.False((await workflow.QuoteAsync(intent with {
            ClientCollectionSessionId = Guid.NewGuid(), SourceIdentity = otherIdentity
        })).Value!.CanRecord); // never resolve liability by vendor display name

        var quote = (await workflow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(p => p.Message)));
        var posted = (await workflow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, posted.Status);
        Assert.StartsWith("SRC-", Assert.Single(posted.Collections).ReferenceCode);
        db.ChangeTracker.Clear();
        Assert.Equal(fineId, Assert.Single(await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.SourceKind == CollectionSourceKind.FineAssessment).ToListAsync()).FineAssessmentId);
        Assert.Equal(0m, (await new CollectionComposerWorkflow(db, collector,
            new Tenant(w.TenantId), clock).QuoteFineAsync(fineId)).Value!.Remaining);
    }
}
