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

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

/// <summary>Source dispatcher, not an amount engine. All writers use this SAME concrete context as the session transaction.</summary>
public sealed class CollectionSessionSources(AppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality, IClock clock, ISender sender) : ICollectionSessionSources
{
    private WcfCollectionWorkflow Water => new(db, user, municipality, clock);
    private GovernedServiceWorkflow Governed => new(db, user, municipality, clock);
    private CollectionComposerWorkflow Composer => new(db, user, municipality, clock);
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
        rows.Add(new(CollectionSessionItemKind.Obligation, "FISH_MEAT_VENDOR_FEE", "Fish/Meat Vendor Fee", true, npm && payorId.HasValue,
            !npm ? "CollectorNotAssigned" : !payorId.HasValue ? "RequiresPayor" : null, "Select an existing linked monthly vendor account.", true, ["AccountId", "Year", "Month", "Amount"]));
        foreach (var (code, name) in new[]
        {
            ("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"), ("FIESTA_ARAW_LOT_RENTAL", "Fiesta/Araw"),
            (CollectorOperationCodes.NpmDaily, "NPM Daily"), ("NPM_WHOLE_PAYMENT", "NPM Whole payment"),
            (CollectorOperationCodes.Tabo, "Tabo"), (CollectorOperationCodes.Slaughterhouse, "Slaughterhouse")
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
                obligationSources = await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, accounts, date, ct);
                var billIds = await db.UtilityBills.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                    && x.Stall!.Contracts.Any(c => c.PayorId == payer)).Select(x => x.Id).ToListAsync(ct);
                foreach (var bill in billIds)
                {
                    // One malformed bill must not hide other valid bills. Existing composer resolves period ownership.
                    var response = await Composer.QuoteMobileEcfAsync(bill, date, ct);
                    if (response.IsSuccess && response.Value!.PayorId == payer) electricitySources.Add(response.Value!);
                }
            }
        }
        return new(payorId, date, rows, waterSources, electricitySources, obligationSources, terms);
    }

    public async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        (CollectionSessionItemQuote?, CollectionSessionProblem?) Fail(string code, string message) => (null, new(item.ClientItemId, code, message));
        var count = (item.Water is null ? 0 : 1) + (item.Service is null ? 0 : 1) + (item.Obligation is null ? 0 : 1) + (item.Electricity is null ? 0 : 1);
        if (count != 1 || item.ConfirmedAmount <= 0m || item.ConfirmedAmount > EEMOCantilanSDS.Domain.Entities.Revenue.Collection.MaximumMoneyAmount
            || decimal.Round(item.ConfirmedAmount, 2) != item.ConfirmedAmount)
            return Fail("InvalidIntent", "Supply one source-specific intent and a positive amount with at most two decimals.");
        if (item.Kind != CollectionSessionItemKind.GovernedService && !session.PayorId.HasValue)
            return Fail("RequiresPayor", "Select the authoritative linked payer before adding this source.");
        CollectionSessionItemQuote Q(string code, string name, string context, RevenueInstrumentType instrument, decimal amount, object version) =>
            new(item.ClientItemId, item.Kind, code, name, context, instrument, amount, JsonSerializer.Serialize(version), Guid.Empty);
        bool Payer(Guid? linked) => session.PayorId is { } selected && linked == selected;
        switch (item.Kind)
        {
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
                var amount = source.PreparedAmount is not null ? source.OutstandingAmount : item.ConfirmedAmount;
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
                var response = await Composer.QuoteMobileEcfAsync(e.UtilityBillId, session.BusinessDate, ct);
                if (!response.IsSuccess) return Fail("InvalidSource", "This electricity bill is not available to you or needs office review.");
                var source = response.Value!;
                if (!Payer(source.PayorId)) return Fail(source.PayorId is null ? "RequiresPayor" : "PayerMismatch", "Select the authoritative linked payer of this bill.");
                if (source.SettlementAuthority != SettlementAuthority.Canonical) return Fail("SourceStillLegacy", "This bill remains on its existing legacy collection path.");
                if (source.ElectricitySourceVersion != e.SourceVersion || !source.CanPostCanonical || item.ConfirmedAmount > source.OutstandingAmount)
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
            case CollectionSessionItemKind.Water:
                var w = item.Water!;
                var source = (await Water.GetMobileSourcesAsync(w.Year, w.Month, ct, w.StallId, session.BusinessDate)).Value!.Single();
                var water = await Water.PostMobileAsync(new(1, operation, session.BusinessDate, source.PreparedAmount is null ? Guid.Empty : w.UtilityBillId ?? Guid.Empty,
                    item.ConfirmedAmount, w.SourceVersion, StallId: w.StallId, BillingYear: w.Year, BillingMonth: w.Month), ct);
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
                var electricity = await Composer.PostMobileEcfAsync(new(operation, e.UtilityBillId, item.ConfirmedAmount, e.SourceVersion, session.BusinessDate), ct);
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
    private Task<bool> NpmAssignedAsync(CancellationToken ct) => db.CollectorUsers.AsNoTracking().AnyAsync(x =>
        x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);
    private async Task<Guid?> PolicyIdAsync(string code, RevenuePolicyContext context, DateOnly date, CancellationToken ct)
    {
        var id = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.IsActive && x.SemanticCode == code)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.RevenueClassificationId == id
            && x.BusinessContext == context && x.EffectiveDate <= date).OrderByDescending(x => x.EffectiveDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }
}
