using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Infrastructure.Fees;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

/// <summary>Source dispatcher, not an amount engine. All writers use this SAME concrete context as the session transaction.</summary>
public sealed class CollectionSessionSources(AppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality, IClock clock, ISender sender, ITpmMarketDayProvider marketDays,
    NpmWholePaymentWorkflow? npmWhole = null) : ICollectionSessionSources
{
    private WcfCollectionWorkflow Water => new(db, user, municipality, clock);
    private GovernedServiceWorkflow Governed => new(db, user, municipality, clock);
    private CollectionComposerWorkflow Composer => new(db, user, municipality, clock);
    private FeeScheduleCollectionWorkflow FeeSchedule => new(db, user, municipality, new FeeRateResolver(db), marketDays, clock);
    private FishMeatVendorFeeCollectionWorkflow VendorFees => new(db, user, municipality, clock);
    private Guid Tenant => municipality.MunicipalityId;

    public async Task<CollectionSessionDiscovery> DiscoverAsync(Guid? payorId, DateOnly date, CancellationToken ct)
    {
        var menu = await sender.Send(new GetCollectorMobileMenuQuery(), ct);
        var operations = await sender.Send(new GetCollectorOperationCapabilitiesQuery(), ct);
        var rows = new List<CollectionSessionCapability>();
        if (operations.IsSuccess)
            foreach (var op in operations.Value!.Operations)
            {
                var water = op.OperationCode == CollectorOperationCodes.Wcf;
                var supported = water || GovernedServiceCatalog.Find(op.OperationCode) is not null && !CollectorOperationCodes.IsFeeSchedule(op.OperationCode);
                rows.Add(new(water ? CollectionSessionItemKind.Water : supported ? CollectionSessionItemKind.GovernedService : null,
                    op.OperationCode, op.Name, supported, supported && op.IsCollectible && (!water || payorId.HasValue),
                    !supported ? "NotSupportedYet" : !op.IsCollectible ? "SourceNotAvailable" : water && !payorId.HasValue ? "RequiresPayor" : null,
                    !supported ? "No itemized adapter is available." : !op.IsCollectible ? "This operation is not currently available to you." : water && !payorId.HasValue ? "Select a linked payer." : null,
                    water, water ? ["StallId", "Year", "Month", "UtilityBillId", "SourceVersion"] : ["ModeIfApplicable", "FeeOptionIdIfApplicable", "VehicleClassIfApplicable", "AmountAccordingToPolicy"]));
            }
        var npm = menu.IsSuccess && menu.Value!.Facilities.Any(f => f.Code == FacilityCode.NPM && f.IsAvailable);
        rows.Add(new(CollectionSessionItemKind.Electricity, "ECF", "Electricity", true, npm && payorId.HasValue,
            !npm ? "CollectorNotAssigned" : !payorId.HasValue ? "RequiresPayor" : null, "Only linked canonical bills with an outstanding balance are eligible.", true, ["UtilityBillId", "SourceVersion", "Amount"]));
        rows.Add(new(CollectionSessionItemKind.VendorFee, "FISH_MEAT_VENDOR_FEE", "Fish/Meat Vendor Fee", true, npm && payorId.HasValue && FishMeatVendorFeeRules.UsesDirectCollection(date),
            !npm ? "CollectorNotAssigned" : !payorId.HasValue ? "RequiresPayor" : null, null, true, ["StallId", "AmountReceived"]));
        rows.Add(new(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", npmWhole is not null, npm && payorId.HasValue && npmWhole is not null,
            npmWhole is null ? "NotSupportedYet" : !npm ? "CollectorNotAssigned" : !payorId.HasValue ? "RequiresPayor" : null, null, true, ["StallId", "Year", "Month"]));
        rows.Add(new(CollectionSessionItemKind.Weighing, "WEIGHT_AND_MEASURE", "Weight & Measure", true, npm && payorId.HasValue,
            !npm ? "CollectorNotAssigned" : !payorId.HasValue ? "RequiresPayor" : null, null, true, ["StallId", "Type", "Kilograms"]));
        foreach (var (code, name) in new[]
        {
            ("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"), ("FIESTA_ARAW_LOT_RENTAL", "Fiesta/Araw"),
            (CollectorOperationCodes.NpmDaily, "NPM Daily"),
            (CollectorOperationCodes.Tabo, "Tabo")
        })
        {
            rows.RemoveAll(x => x.OperationCode == code);
            rows.Add(new(null, code, name, false, false, "NotSupportedYet", "Use this operation's existing collection screen for now.", false, []));
        }
        if (menu.IsSuccess)
            foreach (var facility in menu.Value!.Facilities.Where(x => x.Archetype == BillingArchetype.MonthlyRental))
                rows.Add(new(null, "FACILITY_" + facility.Code, facility.Name, false, false, "NotSupportedYet",
                    "Use this facility's existing collection screen for now.", true, []));
        var terms = new List<CollectionSessionServiceTerms>();
        foreach (var row in rows.Where(x => x.CanAdd && x.Kind == CollectionSessionItemKind.GovernedService))
        {
            var entry = GovernedServiceCatalog.Find(row.OperationCode)!;
            var modes = entry.ModeAware ? new GovernedServiceMode?[] { GovernedServiceMode.WholePayment, GovernedServiceMode.DailyTransaction } : [null];
            foreach (var mode in modes)
            {
                var response = await Governed.GetTermsAsync(row.OperationCode, mode, ct, date);
                if (response.IsSuccess) terms.Add(new(mode, response.Value!));
            }
        }
        IReadOnlyList<WcfMobileSourceDto> waterSources = [];
        var electricitySources = new List<EcfObligationQuoteDto>();
        IReadOnlyList<ObligationQuoteDto> obligationSources = [];
        if (payorId is { } payer)
        {
            var water = await Water.GetMobileSourcesAsync(date.Year, date.Month, ct, businessDate: date, selectedPayorId: payer);
            if (water.IsSuccess) waterSources = water.Value!.Where(x => x.PayorId == payer).ToArray();
            if (npm)
            {
                var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.PayorId == payer && x.Kind == ObligationKind.FishMeatVendorFee).ToListAsync(ct);
                if (!FishMeatVendorFeeRules.UsesDirectCollection(date)) obligationSources = await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, accounts, date, ct);
                var electricity = await Composer.GetMobileEcfSourcesAsync(payer, ct);
                if (electricity.IsSuccess) electricitySources.AddRange(electricity.Value!);
            }
        }
        var weighingSources = new List<WeighingSourceDto>();
        var weighingRates = new List<WeighingRateDto>();
        if (npm)
        {
            var snapshot = await new FeeRateResolver(db).GetSnapshotAsync(ct);
            foreach (var type in new[] { WeighingType.Fish, WeighingType.Meat })
            {
                var rate = snapshot.ResolveEntryOrNull(type == WeighingType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo, date);
                if (rate is { Amount: > 0m })
                {
                    var key = type == WeighingType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo;
                    var rateId = await db.FacilityRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                            && x.FacilityCode == rate.Value.Facility && x.RateKey == key && x.EffectiveDate == rate.Value.EffectiveDate)
                        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                    // The writer requires persisted rate evidence; fallback constants cannot become picker authority.
                    if (rateId.HasValue) weighingRates.Add(new(type, rate.Value.Amount, rate.Value.EffectiveDate, rateId));
                }
            }
            if (payorId.HasValue)
            {
                var stalls = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor)
                    .Where(x => x.MunicipalityId == Tenant && x.Facility!.Code == FacilityCode.NPM
                        && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection)
                        && x.Contracts.Any(c => c.PayorId == payorId)).ToListAsync(ct);
                foreach (var stall in stalls)
                {
                    var contract = stall.OccupancyAnsweringForMonth(date.Year, date.Month, date)?.Contract;
                    if (contract?.PayorId == payorId && contract.Payor?.MunicipalityId == Tenant)
                        weighingSources.Add(new(stall.Id, stall.StallNo, payorId, contract.Payor.DisplayName, "New Public Market", contract.Id, stall.Section));
                }
            }
        }
        var weighingPolicyId = npm && weighingRates.Count > 0
            ? await PolicyIdAsync("WEIGHT_AND_MEASURE", RevenuePolicyContext.Default, date, ct) : null;
        var weighingPolicy = weighingPolicyId.HasValue && await db.RevenueClassificationPolicies.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == Tenant && x.Id == weighingPolicyId && x.PermittedInstrumentType == RevenueInstrumentType.OfficialReceipt, ct);
        var slaughterOptions = new List<SessionSlaughterOption>();
        // SLH is facility-assigned. It deliberately has no CollectorOperationAssignment entry.
        var slaughterAvailable = menu.IsSuccess && menu.Value!.Facilities.Any(x => x.Code == FacilityCode.SLH
            && x.IsAssigned && x.IsAvailable && x.CanonicalCollection);
        rows.RemoveAll(x => x.OperationCode == CollectorOperationCodes.Slaughterhouse);
        rows.Add(new(CollectionSessionItemKind.Slaughter, CollectorOperationCodes.Slaughterhouse, "Slaughterhouse", true,
            slaughterAvailable, slaughterAvailable ? null : "SourceNotAvailable",
            null, false, ["Animal", "Heads", "OwnerNameIfAnonymous"]));
        if (slaughterAvailable)
        {
            var rates = await new FeeRateResolver(db).GetSnapshotAsync(ct);
            foreach (var animal in new[] { AnimalType.Hog, AnimalType.Cow, AnimalType.Carabao })
                if (EEMOCantilanSDS.Application.Common.Slaughterhouse.SlaughterRateKeys.For(animal) is { } key
                    && rates.ResolveOrNull(key, date) is > 0)
                    slaughterOptions.Add(new(animal, animal.ToString(), null));
            slaughterOptions.AddRange(await db.SlaughterAnimalRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.IsActive)
                .Select(x => new SessionSlaughterOption(AnimalType.Other, x.AnimalName, x.AnimalName, null)).ToListAsync(ct));
        }
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].OperationCode == "WEIGHT_AND_MEASURE") rows[i] = rows[i] with
            {
                CanAdd = rows[i].CanAdd && weighingPolicy, StandaloneAvailable = weighingPolicy,
                ReasonCode = rows[i].CanAdd && !weighingPolicy ? "SourceNotAvailable" : rows[i].ReasonCode,
                Reason = rows[i].CanAdd && !weighingPolicy ? "An approved weighing rate and collection policy are required." : rows[i].Reason
            };
            if (rows[i].OperationCode == "ECF") rows[i] = rows[i] with
            { StandaloneAvailable = npm && await PolicyIdAsync(RevenueClassificationCodes.Ecf, RevenuePolicyContext.Default, date, ct) is not null };
        }
        var payerName = payorId is { } selected ? await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == selected).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) : null;
        for (var i = slaughterOptions.Count - 1; i >= 0; i--)
        {
            var option = slaughterOptions[i];
            var quote = await FeeSchedule.QuoteSlaughterAsync(new(Guid.Empty, CollectorOperationCodes.Slaughterhouse, date, 0m,
                Animal: option.Animal, CustomAnimalName: option.CustomAnimalName, Heads: 1,
                OwnerName: payerName ?? "Walk-in"), ct);
            if (!quote.IsSuccess) slaughterOptions.RemoveAt(i);
            else slaughterOptions[i] = option with { Instrument = quote.Value!.Instrument };
        }
        var vendorSources = npm ? (await VendorFees.DiscoverAsync(payorId, ct)).Value ?? [] : [];
        var npmSources = new List<WeighingSourceDto>();
        var npmWholeSources = new List<SessionNpmWholeSource>();
        if (npm && payorId.HasValue && npmWhole is not null)
        {
            var stalls = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor).Where(x => x.MunicipalityId == Tenant
                && x.Facility!.Code == FacilityCode.NPM && x.Contracts.Any(c => c.PayorId == payorId)).ToListAsync(ct);
            foreach (var stall in stalls)
            {
                var occupancy = stall.OccupancyAnsweringForMonth(date.Year, date.Month,
                    new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1))?.Contract;
                if (occupancy?.PayorId == payorId && occupancy.Payor?.MunicipalityId == Tenant)
                {
                    var quote = await npmWhole.QuoteAsync(stall.Id, date.Year, date.Month, ct, date);
                    if (quote.IsSuccess && quote.Value is { Amount: > 0m } value)
                    {
                        npmSources.Add(new(stall.Id, stall.StallNo, payorId, occupancy.Payor.DisplayName, "New Public Market", occupancy.Id, stall.Section));
                        npmWholeSources.Add(new(stall.Id, occupancy.Id, payorId.Value, stall.StallNo, occupancy.Payor.DisplayName,
                            date.Year, date.Month, value.Instrument, value.Amount, value.MonthlyObligation, value.Collected, value.Credits));
                    }
                }
            }
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var hasLinkedSource = rows[i].Kind switch
            {
                CollectionSessionItemKind.Water => waterSources.Any(x => x.CanCollect || x.CanEnterDirect),
                CollectionSessionItemKind.Electricity => electricitySources.Any(x => x.CanPostCanonical),
                CollectionSessionItemKind.VendorFee => vendorSources.Any(x => x.CanCollect),
                CollectionSessionItemKind.Weighing => weighingSources.Count > 0,
                CollectionSessionItemKind.NpmWholePayment => npmSources.Count > 0,
                _ => true
            };
            if (rows[i].CanAdd && !hasLinkedSource)
                rows[i] = rows[i] with { CanAdd = false, ReasonCode = "NoEligibleSource", Reason = "No eligible linked source is available for this payer." };
        }
        return CollectionSessionChoiceProjection.Apply(new(payorId, date, rows, waterSources, electricitySources, obligationSources,
            terms, weighingSources, weighingRates, slaughterOptions, payerName, vendorSources, npmSources, npmWholeSources));
    }

    public async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        (CollectionSessionItemQuote?, CollectionSessionProblem?) Fail(string code, string message) => (null, new(item.ClientItemId, code, message));
        var count = (item.Water is null ? 0 : 1) + (item.Service is null ? 0 : 1) + (item.Obligation is null ? 0 : 1) + (item.Electricity is null ? 0 : 1) + (item.Weighing is null ? 0 : 1) + (item.Slaughter is null ? 0 : 1) + (item.VendorFee is null ? 0 : 1) + (item.NpmWhole is null ? 0 : 1);
        if (count != 1 || (item.Kind is CollectionSessionItemKind.Weighing or CollectionSessionItemKind.Slaughter or CollectionSessionItemKind.NpmWholePayment ? item.ConfirmedAmount < 0m : item.ConfirmedAmount <= 0m) || item.ConfirmedAmount > EEMOCantilanSDS.Domain.Entities.Revenue.Collection.MaximumMoneyAmount
            || decimal.Round(item.ConfirmedAmount, 2) != item.ConfirmedAmount)
            return Fail("InvalidIntent", "Supply one source-specific intent and a positive amount with at most two decimals.");
        if (item.Kind is not (CollectionSessionItemKind.GovernedService or CollectionSessionItemKind.Slaughter) && !session.PayorId.HasValue)
            return Fail("RequiresPayor", "Select the authoritative linked payer before adding this source.");
        CollectionSessionItemQuote Q(string code, string name, string context, RevenueInstrumentType instrument, decimal amount, object version) =>
            new(item.ClientItemId, item.Kind, code, name, context, instrument, amount, JsonSerializer.Serialize(version), Guid.Empty);
        bool Payer(Guid? linked) => session.PayorId is { } selected && linked == selected;
        switch (item.Kind)
        {
            case CollectionSessionItemKind.VendorFee when item.VendorFee is { } vendor:
            {
                var stall = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor)
                    .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == vendor.StallId && x.Facility!.Code == FacilityCode.NPM
                        && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection), ct);
                if (stall is null) return Fail("InvalidSource", "The selected vendor is not available.");
                var occupancy = stall.OccupancyAnsweringForMonth(session.BusinessDate.Year, session.BusinessDate.Month, session.BusinessDate)?.Contract;
                if (!Payer(occupancy?.PayorId) || occupancy?.Payor?.MunicipalityId != Tenant) return Fail("PayerMismatch", "Select the vendor's linked payer.");
                var result = await VendorFees.QuoteAsync(new(item.ClientItemId, session.BusinessDate, vendor.StallId, session.PayorId!.Value, item.ConfirmedAmount), ct);
                if (!result.IsSuccess) return Fail("SourceNotAvailable", "Select an eligible linked vendor and a positive amount received.");
                var quote = result.Value!;
                return (Q("FISH_MEAT_VENDOR_FEE", "Fish / Meat Vendor Fee", quote.Source.PayerName + " · Stall " + quote.Source.StallNo, quote.Instrument, quote.Amount, quote.Version), null);
            }
            case CollectionSessionItemKind.NpmWholePayment when item.NpmWhole is { } n:
            {
                if (npmWhole is null) return Fail("NotSupportedYet", "Whole payment is not available in this checkout.");
                if (n.Year is < 2000 or > 2100 || n.Month is < 1 or > 12) return Fail("InvalidIntent", "Choose a valid period.");
                var stall = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor).SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == n.StallId && x.Facility!.Code == FacilityCode.NPM, ct);
                var occupancy = stall?.OccupancyAnsweringForMonth(n.Year, n.Month, new DateOnly(n.Year, n.Month, 1).AddMonths(1).AddDays(-1))?.Contract;
                if (!Payer(occupancy?.PayorId) || occupancy?.Payor?.MunicipalityId != Tenant) return Fail("PayerMismatch", "The selected payer does not own this period.");
                var result = await npmWhole.QuoteAsync(n.StallId, n.Year, n.Month, ct, session.BusinessDate);
                if (!result.IsSuccess || result.Value?.Amount is not > 0m) return Fail("SourceNotAvailable", "The month has no collectible remaining rent.");
                var quote = result.Value!;
                return (Q("NPM_WHOLE_PAYMENT", "NPM Whole payment", new DateOnly(n.Year, n.Month, 1).ToString("MMMM yyyy"), quote.Instrument, quote.Amount, quote.QuoteToken),
                    item.ConfirmedAmount != 0m && item.ConfirmedAmount != quote.Amount ? new(item.ClientItemId, "AmountChanged", "Review the current remaining rent.") : null);
            }
            case CollectionSessionItemKind.Slaughter when item.Slaughter is { } slaughter:
            {
                var owner = session.PayorId is { } payer ? await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == payer).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) : slaughter.OwnerName;
                var response = await FeeSchedule.QuoteSlaughterAsync(new(Guid.Empty, CollectorOperationCodes.Slaughterhouse, session.BusinessDate, 0,
                    Animal: slaughter.Animal, CustomAnimalName: slaughter.CustomAnimalName, Heads: slaughter.Heads, OwnerName: owner), ct);
                if (!response.IsSuccess) return Fail("SourceNotAvailable", "Review the approved animal, head count and owner before continuing.");
                var quote = response.Value!;
                return (Q(CollectorOperationCodes.Slaughterhouse, "Slaughterhouse", quote.Context, quote.Instrument, quote.Amount, quote.Version),
                    item.ConfirmedAmount != 0m && item.ConfirmedAmount != quote.Amount ? new(item.ClientItemId, "AmountChanged", "Review the approved slaughter amount.") : null);
            }
            case CollectionSessionItemKind.Weighing when item.Weighing is { } weighing:
            {
                var facts = await new WeighingCollectionSource(db).QuoteAsync(Tenant, user.CollectorId!.Value,
                    session.BusinessDate, session.PayorId, weighing, ct);
                if (facts is null) return Fail("SourceNotAvailable", "Select a linked weighing vendor and an approved effective rate.");
                return (Q("WEIGHT_AND_MEASURE", "Weight & Measure", $"{facts.Context} · {weighing.Type}",
                    RevenueInstrumentType.OfficialReceipt, facts.Amount, facts.Snapshot),
                    item.ConfirmedAmount != 0m && item.ConfirmedAmount != facts.Amount
                        ? new(item.ClientItemId, "AmountChanged", "Review the approved weighing amount.") : null);
            }
            case CollectionSessionItemKind.Water when item.Water is { } w:
            {
                var response = await Water.GetMobileSourcesAsync(w.Year, w.Month, ct, w.StallId, session.BusinessDate);
                if (!response.IsSuccess) return Fail("SourceNotAvailable", "This water source is not available to you.");
                var source = response.Value!.SingleOrDefault();
                if (source is null) return Fail("InvalidSource", "The selected water source is not available.");
                if (!Payer(source.PayorId)) return Fail(source.PayorId is null ? "RequiresPayor" : "PayerMismatch", "Select the authoritative linked payer of this source.");
                if (!source.CanCollect && !source.CanEnterDirect) return Fail("SourceStillLegacy", "This water source needs office review or has no remaining balance.");
                if (source.UtilityBillId != w.UtilityBillId || source.WaterSourceVersion != w.SourceVersion)
                    return Fail("BalanceChanged", "The water source changed. Refresh the source before continuing.");
                var amount = item.ConfirmedAmount;
                if (source.PreparedAmount is not null && amount > source.OutstandingAmount)
                    return Fail("BalanceChanged", "The amount exceeds the prepared Water balance.");
                if (source.PreparedAmount is null && (w.Year != session.BusinessDate.Year || w.Month != session.BusinessDate.Month))
                    return Fail("InvalidPeriod", "A direct water amount is recorded for the collection's business month only.");
                if (amount <= 0 || amount > WcfCollectionWorkflow.MaximumDirectCollectionAmount) return Fail("AmountChanged", "The water amount is not collectible.");
                var policy = await PolicyIdAsync(RevenueClassificationCodes.Wcf, RevenuePolicyContext.Default, session.BusinessDate, ct);
                var quote = Q("WCF", "Water", $"{source.StallNo} · {w.Year}-{w.Month:00}", RevenueInstrumentType.CashTicket, amount,
                    new { source.UtilityBillId, source.WaterSourceVersion, source.PreparedAmount, source.SettledAmount, source.OutstandingAmount, source.CanEnterDirect, Policy = policy });
                return (quote, amount != item.ConfirmedAmount ? new(item.ClientItemId, "AmountChanged", "The office-prepared water amount wins. Review its remaining amount.") : null);
            }
            case CollectionSessionItemKind.GovernedService when item.Service is { } g:
            {
                var entry = GovernedServiceCatalog.Find((g.OperationCode ?? "").Trim().ToUpperInvariant());
                if (entry is null || CollectorOperationCodes.IsFeeSchedule(entry.Code)) return Fail("NotSupportedYet", "This specialized source has no itemized adapter yet.");
                var response = await Governed.GetTermsAsync(entry.Code, g.Mode, ct, session.BusinessDate);
                if (!response.IsSuccess) return Fail("SourceNotAvailable", "This operation is not available under your assignment and the effective policy.");
                var terms = response.Value!;
                decimal amount;
                if (g.Reference?.Trim().Length > 200) return Fail("InvalidIntent", "The reference must not exceed 200 characters.");
                if (terms.Basis == GovernedServiceBasis.VehicleClassRate)
                {
                    if (g.FeeOptionId.HasValue) return Fail("InvalidIntent", "This source does not take a fee option.");
                    var rate = terms.VehicleClasses?.SingleOrDefault(x => x.Code == g.VehicleClassCode?.Trim().ToUpperInvariant());
                    if (rate is null) return Fail("InvalidSource", "Select an approved vehicle class.");
                    amount = rate.Amount;
                }
                else if (terms.Basis == GovernedServiceBasis.ApprovedFeeOption)
                {
                    if (!string.IsNullOrWhiteSpace(g.VehicleClassCode)) return Fail("InvalidIntent", "This source does not take a vehicle class.");
                    var option = terms.FeeOptions?.SingleOrDefault(x => x.Id == g.FeeOptionId);
                    if (option is null) return Fail("InvalidSource", "Select a currently approved fee option.");
                    amount = option.Basis == GovernedServiceBasis.FixedAmount ? option.Amount!.Value : item.ConfirmedAmount;
                    if (option.MaximumAmount is { } max && amount > max) return Fail("AmountAboveCeiling", "The amount exceeds the approved fee ceiling.");
                }
                else
                {
                    if (g.FeeOptionId.HasValue || !string.IsNullOrWhiteSpace(g.VehicleClassCode)) return Fail("InvalidIntent", "This source does not take those fee or vehicle inputs.");
                    amount = terms.Basis == GovernedServiceBasis.FixedAmount ? terms.FixedAmount!.Value : item.ConfirmedAmount;
                    if (terms.MaximumAmount is { } max && amount > max) return Fail("AmountAboveCeiling", "The amount exceeds the approved service ceiling.");
                }
                // Reuse approved policy identities for stale detection, not labels or client classifications.
                var service = await db.GovernedServices.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.OperationCode == entry.Code, ct);
                var settings = await db.GovernedServiceSettings.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.GovernedServiceId == service.Id).ToListAsync(ct);
                var context = GovernedServiceCatalog.ContextFor(entry, g.Mode);
                var classificationId = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.SemanticCode == entry.ClassificationCode && x.IsActive).Select(x => x.Id).SingleAsync(ct);
                var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.RevenueClassificationId == classificationId && x.BusinessContext == context && x.EffectiveDate <= session.BusinessDate)
                    .OrderByDescending(x => x.EffectiveDate).Select(x => x.Id).FirstAsync(ct);
                Guid? rateVersion = null;
                if (g.FeeOptionId is { } optionId)
                    rateVersion = GovernedServiceFeeOptionRate.Resolve(await db.GovernedServiceFeeOptionRates.AsNoTracking()
                        .Where(x => x.MunicipalityId == Tenant && x.FeeOptionId == optionId).ToListAsync(ct), session.BusinessDate)?.Id;
                if (!string.IsNullOrWhiteSpace(g.VehicleClassCode))
                {
                    var code = g.VehicleClassCode.Trim().ToUpperInvariant();
                    rateVersion = VehicleClassRate.Resolve(await (from rate in db.VehicleClassRates.AsNoTracking()
                        join vehicle in db.VehicleClasses.AsNoTracking() on rate.VehicleClassId equals vehicle.Id
                        where rate.MunicipalityId == Tenant && vehicle.MunicipalityId == Tenant && vehicle.Code == code
                        select rate).ToListAsync(ct), session.BusinessDate)?.Id;
                }
                return (Q(entry.Code, entry.Name, g.Reference ?? entry.Name, terms.Instrument, amount,
                    new { Setting = GovernedServiceSetting.Resolve(settings, session.BusinessDate)!.Id, Policy = policy,
                        RateVersion = rateVersion, terms.Basis, terms.FixedAmount, terms.MaximumAmount, g.FeeOptionId,
                        VehicleClassCode = g.VehicleClassCode?.Trim().ToUpperInvariant(), ApprovedAmount = amount }),
                    amount != item.ConfirmedAmount ? new(item.ClientItemId, "AmountChanged", "Review the current approved amount.") : null);
            }
            case CollectionSessionItemKind.Obligation when item.Obligation is { } o:
            {
                if (FishMeatVendorFeeRules.UsesDirectCollection(session.BusinessDate)) return Fail("SourceStillLegacy", "Monthly vendor-fee accounts are historical. Choose direct Vendor Fee.");
                if (!await NpmAssignedAsync(ct)) return Fail("CollectorNotAssigned", "This source is not assigned to you.");
                var account = await db.ObligationAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == o.AccountId, ct);
                if (account is null) return Fail("InvalidSource", "The selected account is not available.");
                if (account.Kind != ObligationKind.FishMeatVendorFee) return Fail("NotSupportedYet", "This account has no authorized Collector Mobile adapter yet.");
                if (!Payer(account.PayorId)) return Fail("PayerMismatch", "Select the authoritative linked payer of this account.");
                var quote = (await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, [account], session.BusinessDate, ct))
                    .SingleOrDefault(x => x.PeriodStart.Year == o.Year && x.PeriodStart.Month == o.Month);
                if (quote is null || !quote.CanAddToDraft) return Fail("BalanceChanged", "This period has no collectible balance.");
                if (item.ConfirmedAmount > quote.OutstandingAmount) return Fail("BalanceChanged", "The amount exceeds the period's remaining balance.");
                var classificationId = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.RevenueClassificationId == classificationId && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= session.BusinessDate)
                    .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
                if (policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt) return Fail("SourceNotAvailable", "No approved vendor-fee policy is effective.");
                return (Q("FISH_MEAT_VENDOR_FEE", "Fish/Meat Vendor Fee", $"{quote.SubjectLabel} · {o.Year}-{o.Month:00}",
                    RevenueInstrumentType.OfficialReceipt, item.ConfirmedAmount, new { quote.AccountId, quote.PeriodId,
                        quote.RateId, quote.AssessedAmount, quote.SettledAmount, quote.OutstandingAmount, Policy = policy.Id }), null);
            }
            case CollectionSessionItemKind.Electricity when item.Electricity is { } e:
            {
                var response = e.UtilityBillId == Guid.Empty
                    ? await QuoteDirectElectricityAsync(e, session.PayorId, ct)
                    : await Composer.QuoteMobileEcfAsync(e.UtilityBillId, session.BusinessDate, ct);
                if (!response.IsSuccess) return Fail("InvalidSource", "This electricity bill is not available to you or needs office review.");
                var source = response.Value!;
                if (!Payer(source.PayorId)) return Fail(source.PayorId is null ? "RequiresPayor" : "PayerMismatch", "Select the authoritative linked payer of this bill.");
                if (!source.CanPostCanonical) return Fail("SourceStillLegacy", "This Electricity source needs office review or has no remaining balance.");
                if (source.ElectricitySourceVersion != e.SourceVersion || source.ChargeBasis != "DirectCollection" && item.ConfirmedAmount > source.OutstandingAmount)
                    return Fail("BalanceChanged", "The electricity bill or remaining balance changed.");
                return (Q("ECF", "Electricity", $"{source.StallNo} · {source.BillingYear}-{source.BillingMonth:00}",
                    source.Instrument, item.ConfirmedAmount, new { source.UtilityBillId, source.ElectricitySourceVersion,
                        source.AssessedAmount, source.CumulativeSettledEvidence, source.OutstandingAmount, source.RevenueClassificationPolicyId }), null);
            }
            default: return Fail("NotSupportedYet", "This source has no itemized adapter yet.");
        }
    }

    public async Task<CollectionSessionCollection> PostAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        Guid id; string reference; decimal amount;
        switch (item.Kind)
        {
            case CollectionSessionItemKind.VendorFee:
                var vendor = await VendorFees.PostAsync(new(operation, session.BusinessDate, item.VendorFee!.StallId, session.PayorId!.Value, item.ConfirmedAmount), ct);
                if (!vendor.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The vendor fee source changed. Review the checkout.");
                (id, reference, amount) = (vendor.Value!.CollectionId, vendor.Value.ReferenceCode, vendor.Value.Amount); break;
            case CollectionSessionItemKind.NpmWholePayment:
                var n = item.NpmWhole!;
                var rentQuote = await npmWhole!.QuoteAsync(n.StallId, n.Year, n.Month, ct, session.BusinessDate);
                if (!rentQuote.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The rent balance changed. Review the checkout.");
                var rent = await npmWhole.PostAsync(new(operation, n.StallId, n.Year, n.Month, session.BusinessDate, rentQuote.Value!.Amount, rentQuote.Value.QuoteToken), ct);
                if (!rent.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The rent balance changed. Review the checkout.");
                (id, reference, amount) = (rent.Value!.CollectionId, rent.Value.ReferenceCode, rent.Value.Amount); break;
            case CollectionSessionItemKind.Slaughter:
                var slaughterIntent = item.Slaughter!;
                var slaughterOwner = session.PayorId is { } selected ? await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == selected).Select(x => x.DisplayName).SingleAsync(ct) : slaughterIntent.OwnerName;
                var slaughterRequest = new FeeSchedulePostRequest(operation, CollectorOperationCodes.Slaughterhouse, session.BusinessDate, 0,
                    Animal: slaughterIntent.Animal, CustomAnimalName: slaughterIntent.CustomAnimalName, Heads: slaughterIntent.Heads, OwnerName: slaughterOwner);
                var slaughterQuote = await FeeSchedule.QuoteSlaughterAsync(slaughterRequest, ct);
                if (!slaughterQuote.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The slaughter transaction changed. Review the checkout.");
                var slaughter = await FeeSchedule.PostMobileAsync(slaughterRequest with { ReceivedAmount = slaughterQuote.Value!.Amount }, ct);
                if (!slaughter.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The slaughter transaction changed. Review the checkout.");
                (id, reference, amount) = (slaughter.Value!.CollectionId, slaughter.Value.ReferenceCode, slaughter.Value.Amount); break;
            case CollectionSessionItemKind.Weighing:
                var weighingSource = new WeighingCollectionSource(db);
                var facts = await weighingSource.QuoteAsync(Tenant, user.CollectorId!.Value, session.BusinessDate,
                    session.PayorId, item.Weighing!, ct);
                if (facts is null) throw new CollectionSessionPostingException(item.ClientItemId, "The weighing source changed. Review the checkout.");
                var weighing = await weighingSource.PostAsync(Tenant, user.CollectorId!.Value, user.Username ?? "Collector",
                    session.BusinessDate, operation, item.Weighing!, facts, ct);
                (id, reference, amount) = (weighing.Id, weighing.ReferenceCode, weighing.TotalAmount); break;
            case CollectionSessionItemKind.Water:
                var w = item.Water!;
                var source = (await Water.GetMobileSourcesAsync(w.Year, w.Month, ct, w.StallId, session.BusinessDate)).Value!.Single();
                var waterVersion = w.SourceVersion;
                if (w.UtilityBillId is null || w.UtilityBillId == Guid.Empty)
                    if (await SiblingCreatedUtilityBillAsync(session, item, w.StallId, w.Year, w.Month, ct) is { } siblingWaterBill)
                        waterVersion = siblingWaterBill.WaterSourceVersion;
                // WCF's existing writer distinguishes direct mode by an empty bill ID and resolves its stall/month itself.
                var water = await Water.PostMobileAsync(new(1, operation, session.BusinessDate, source.PreparedAmount is null ? Guid.Empty : w.UtilityBillId ?? Guid.Empty,
                    item.ConfirmedAmount, waterVersion, StallId: w.StallId, BillingYear: w.Year, BillingMonth: w.Month), ct);
                if (!water.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "Water changed during posting. Review the checkout.");
                (id, reference, amount) = (water.Value!.CollectionId, water.Value.ReferenceCode, water.Value.Amount); break;
            case CollectionSessionItemKind.GovernedService:
                var g = item.Service!;
                var name = session.PayorId is { } payor ? await db.Payors.AsNoTracking().Where(x => x.Id == payor && x.MunicipalityId == Tenant).Select(x => x.DisplayName).SingleAsync(ct) : null;
                var service = await Governed.PostMobileAsync(new(1, operation, g.OperationCode.Trim().ToUpperInvariant(), session.BusinessDate,
                    item.ConfirmedAmount, g.Mode, name, g.Reference, VehicleClassCode: g.VehicleClassCode, FeeOptionId: g.FeeOptionId), ct);
                if (!service.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The service changed during posting. Review the checkout.");
                (id, reference, amount) = (service.Value!.CollectionId, service.Value.ReferenceCode, service.Value.Amount); break;
            case CollectionSessionItemKind.Obligation:
                var o = item.Obligation!;
                var obligation = await Composer.PostMobileObligationAsync(new(operation, o.AccountId, o.Year, o.Month, item.ConfirmedAmount, session.BusinessDate), ct);
                if (!obligation.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The account changed during posting. Review the checkout.");
                (id, reference, amount) = (obligation.Value!.CollectionId, obligation.Value.ReferenceCode, obligation.Value.Amount); break;
            case CollectionSessionItemKind.Electricity:
                var e = item.Electricity!;
                var electricityBill = e.UtilityBillId;
                var electricityVersion = e.SourceVersion;
                if (electricityBill == Guid.Empty && e.StallId is { } electricityStall && e.Year is { } electricityYear && e.Month is { } electricityMonth)
                    if (await SiblingCreatedUtilityBillAsync(session, item, electricityStall, electricityYear, electricityMonth, ct) is { } siblingElectricityBill)
                    {
                        electricityBill = siblingElectricityBill.Id;
                        electricityVersion = siblingElectricityBill.ElectricitySourceVersion;
                    }
                var electricity = await Composer.PostMobileEcfAsync(new(operation, electricityBill, item.ConfirmedAmount, electricityVersion, session.BusinessDate, e.StallId, e.Year, e.Month), ct);
                if (!electricity.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "The bill changed during posting. Review the checkout.");
                (id, reference, amount) = (electricity.Value!.CollectionId, electricity.Value.ReferenceCode, electricity.Value.Amount); break;
            default: throw new CollectionSessionPostingException(item.ClientItemId, "This source is not supported.");
        }
        var instrument = await (from line in db.CollectionLines.AsNoTracking()
            join policy in db.RevenueClassificationPolicies.AsNoTracking() on line.RevenueClassificationPolicyId equals policy.Id
            where line.MunicipalityId == Tenant && line.CollectionId == id && policy.MunicipalityId == Tenant
            select policy.PermittedInstrumentType).FirstAsync(ct);
        return new(id, reference, instrument!.Value, amount, [item.ClientItemId], "Posted");
    }
    /// <summary>
    /// An originally absent bill can be materialized by the other utility child in THIS atomic checkout.
    /// Resolve it only through that child's deterministic posting identity, never by accepting an arbitrary newer bill.
    /// The provisional version is replaced by that newly created bill's initial part version. Existing bill versions
    /// are never replaced. Canonical clean/direct checks still apply, and session replay precedes dispatch.
    /// </summary>
    private async Task<EEMOCantilanSDS.Domain.Entities.Payments.UtilityBill?> SiblingCreatedUtilityBillAsync(CollectionSessionIntent session, CollectionSessionItemIntent current,
        Guid stallId, int year, int month, CancellationToken ct)
    {
        if (!db.HasActiveTransaction) return null;
        var siblings = session.Items.Where(x => x.ClientItemId != current.ClientItemId &&
            (current.Kind == CollectionSessionItemKind.Electricity && x.Kind == CollectionSessionItemKind.Water
                && x.Water is { } w && w.StallId == stallId && w.Year == year && w.Month == month && (w.UtilityBillId is null || w.UtilityBillId == Guid.Empty)
             || current.Kind == CollectionSessionItemKind.Water && x.Kind == CollectionSessionItemKind.Electricity
                && x.Electricity is { } e && e.StallId == stallId && e.Year == year && e.Month == month && e.UtilityBillId == Guid.Empty))
            .Select(x => CollectionSessionWorkflow.ChildOperationId(Tenant, session.ClientCollectionSessionId, x.ClientItemId)).ToArray();
        if (siblings.Length == 0) return null;
        return await (from collection in db.Collections.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join bill in db.UtilityBills.AsNoTracking() on line.SourceId equals bill.Id
            where collection.MunicipalityId == Tenant && line.MunicipalityId == Tenant && bill.MunicipalityId == Tenant
                && collection.ClientOperationId.HasValue && siblings.Contains(collection.ClientOperationId.Value)
                && line.SourceKind == CollectionSourceKind.UtilityBill && bill.StallId == stallId && bill.BillingYear == year && bill.BillingMonth == month
            select bill).Distinct().SingleOrDefaultAsync(ct);
    }
    private Task<bool> NpmAssignedAsync(CancellationToken ct) => db.CollectorUsers.AsNoTracking().AnyAsync(x =>
        x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);
    private async Task<Result<EcfObligationQuoteDto>> QuoteDirectElectricityAsync(SessionElectricityIntent intent, Guid? payer, CancellationToken ct)
    {
        var result = await Composer.GetMobileEcfSourcesAsync(payer, ct);
        var source = result.IsSuccess ? result.Value!.SingleOrDefault(x => x.StallId == intent.StallId
            && x.BillingYear == intent.Year && x.BillingMonth == intent.Month && x.UtilityBillId == Guid.Empty) : null;
        return source is null ? Result<EcfObligationQuoteDto>.NotFound() : Result<EcfObligationQuoteDto>.Success(source);
    }
    private async Task<Guid?> PolicyIdAsync(string code, RevenuePolicyContext context, DateOnly date, CancellationToken ct)
    {
        var id = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.IsActive && x.SemanticCode == code)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.RevenueClassificationId == id
            && x.BusinessContext == context && x.EffectiveDate <= date).OrderByDescending(x => x.EffectiveDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }
}
