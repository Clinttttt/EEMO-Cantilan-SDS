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
        new(RevenueClassificationCodes.Tabo, "Tabo", RevenueInstrumentType.CashTicket),
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
            .Select(x => x.RevenueClassificationId)
            .ToListAsync();
        var hasPolicy = existingPolicies.ToHashSet();

        foreach (var definition in ConfirmedCantilanDefinitions)
        {
            var classification = byCode[definition.Code];
            if (hasPolicy.Contains(classification.Id)) continue;

            context.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id,
                seedDate,
                definition.DisplayName,
                definition.InstrumentType,
                cantilanId));
        }

        await context.SaveChangesAsync();
    }
}
