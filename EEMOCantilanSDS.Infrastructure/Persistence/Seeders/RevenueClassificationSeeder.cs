using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds only the confirmed Cantilan revenue semantics and its approved OR/Cash Ticket policy.
/// These rows are dormant configuration and do not alter existing collection/report flows.
/// </summary>
public static class RevenueClassificationSeeder
{
    private static readonly DateOnly InstrumentClarificationDate = new(2026, 9, 27);

    private sealed record SeedDefinition(
        string Code,
        string DisplayName,
        RevenueInstrumentType? InstrumentType);

    private static readonly SeedDefinition[] ConfirmedCantilanDefinitions =
    [
        new(RevenueClassificationCodes.PermanentStallRent, "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.Ecf, "ECF", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.FishMeatVendorFee, "Fish/Meat Vendor Fee", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.WeightAndMeasure, "Weight & Measure", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.PenaltiesAndFines, "Penalties/Fines", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.Slaughterhouse, "Slaughterhouse", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.MarketFees, "Market Fees", RevenueInstrumentType.CashTicket),
        new(RevenueClassificationCodes.Tabo, "Tabo", RevenueInstrumentType.OfficialReceipt),
        new(RevenueClassificationCodes.TransportationParking, "Transportation/Parking", RevenueInstrumentType.CashTicket),
        new(RevenueClassificationCodes.VegetableFruitSpaceRental, "Vegetable/Fruit Space Rental", RevenueInstrumentType.CashTicket),
        new(RevenueClassificationCodes.Wcf, "WCF", RevenueInstrumentType.CashTicket),
        new(RevenueClassificationCodes.LandingBerthing, "Landing/Berthing", RevenueInstrumentType.CashTicket),
        new(RevenueClassificationCodes.Arrears, "Arrears", null)
    ];

    public static async Task SeedAsync(IAppDbContext context, DateOnly? effectiveDate = null)
    {
        // Cross-tenant startup read is explicitly limited to the authoritative tenant code. The instrument
        // mappings below must never be copied to every municipality or inferred from IsDefault/activation.
        var cantilanId = await context.Municipalities
            .IgnoreQueryFilters()
            .Where(x => x.Code == "CANTILAN")
            .Select(x => x.Id)
            .SingleOrDefaultAsync();

        if (cantilanId == Guid.Empty) return;

        var seedDate = effectiveDate ?? PhilippineTime.Today;
        var existingClassifications = await context.RevenueClassifications
            .IgnoreQueryFilters()
            .Where(x => x.MunicipalityId == cantilanId)
            .ToListAsync();

        var byCode = existingClassifications.ToDictionary(x => x.SemanticCode, StringComparer.Ordinal);
        foreach (var definition in ConfirmedCantilanDefinitions)
        {
            if (!byCode.TryGetValue(definition.Code, out var classification))
            {
                classification = RevenueClassification.Create(definition.Code, cantilanId);
                context.RevenueClassifications.Add(classification);
                byCode.Add(definition.Code, classification);
            }
        }

        var classificationIds = byCode.Values.Select(x => x.Id).ToHashSet();
        var existingPolicies = await context.RevenueClassificationPolicies
            .IgnoreQueryFilters()
            .Where(x => x.MunicipalityId == cantilanId && classificationIds.Contains(x.RevenueClassificationId))
            .ToListAsync();
        var defaultPolicies = existingPolicies
            .Where(x => x.BusinessContext == RevenuePolicyContext.Default)
            .ToList();
        var hasDefaultPolicy = defaultPolicies.Select(x => x.RevenueClassificationId).ToHashSet();

        foreach (var definition in ConfirmedCantilanDefinitions)
        {
            var classification = byCode[definition.Code];
            if (hasDefaultPolicy.Contains(classification.Id)
                || definition.Code == RevenueClassificationCodes.VegetableFruitSpaceRental && seedDate >= InstrumentClarificationDate)
                continue;

            var policyEffectiveDate = definition.Code == RevenueClassificationCodes.Tabo && seedDate >= InstrumentClarificationDate
                ? InstrumentClarificationDate
                : seedDate;
            var instrument = definition.Code == RevenueClassificationCodes.Tabo && seedDate < InstrumentClarificationDate
                ? RevenueInstrumentType.CashTicket
                : definition.InstrumentType;

            context.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id,
                policyEffectiveDate,
                definition.DisplayName,
                instrument,
                cantilanId));
        }

        // Tabo's earlier CT policy is immutable historical evidence. Append the confirmed OR policy
        // at the clarification date only when the existing default stream is entirely pre-clarification
        // and its latest version is the superseded CT assumption.
        var taboId = byCode[RevenueClassificationCodes.Tabo].Id;
        var taboPolicies = defaultPolicies.Where(x => x.RevenueClassificationId == taboId).ToList();
        if (seedDate >= InstrumentClarificationDate
            && taboPolicies.Count > 0
            && taboPolicies.All(x => x.EffectiveDate < InstrumentClarificationDate)
            && taboPolicies.OrderByDescending(x => x.EffectiveDate).First().PermittedInstrumentType
                == RevenueInstrumentType.CashTicket)
        {
            context.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                taboId,
                InstrumentClarificationDate,
                "Tabo",
                RevenueInstrumentType.OfficialReceipt,
                cantilanId,
                createdBy: "System"));
        }

        // Existing single-instrument Vegetable/Fruit rows remain historical compatibility evidence.
        // A fresh post-clarification setup does not receive a new Default instrument row because one
        // instrument cannot represent both modes. Writers must resolve one of the explicit contexts below.
        if (seedDate >= InstrumentClarificationDate)
        {
            var vegetableId = byCode[RevenueClassificationCodes.VegetableFruitSpaceRental].Id;
            foreach (var (businessContext, instrument) in new[]
            {
                (RevenuePolicyContext.VegetableWholePayment, RevenueInstrumentType.OfficialReceipt),
                (RevenuePolicyContext.VegetableDailyTransaction, RevenueInstrumentType.CashTicket)
            })
            {
                if (existingPolicies.Any(x => x.RevenueClassificationId == vegetableId
                        && x.BusinessContext == businessContext
                        && x.EffectiveDate == InstrumentClarificationDate))
                    continue;

                context.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                    vegetableId,
                    InstrumentClarificationDate,
                    "Vegetable/Fruit Space Rental",
                    instrument,
                    cantilanId,
                    description: "Instrument determined by the confirmed payment context.",
                    createdBy: "System",
                    businessContext: businessContext));
            }
        }

        await context.SaveChangesAsync();
    }
}
