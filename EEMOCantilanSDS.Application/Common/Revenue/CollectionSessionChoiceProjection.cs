using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Projects already resolved source facts into typed picker rows. Never quotes, prices or authorizes money.</summary>
public static class CollectionSessionChoiceProjection
{
    public static CollectionSessionDiscovery Apply(CollectionSessionDiscovery discovery) => discovery with
    {
        Operations = discovery.Operations.Select(operation =>
        {
            var choices = operation.CanAdd ? Choices(discovery, operation).OrderBy(x => x.SelectionKey, StringComparer.Ordinal).ToArray() : [];
            return operation with
            {
                Choices = choices,
                CanAdd = operation.CanAdd && choices.Length > 0,
                ReasonCode = operation.CanAdd && choices.Length == 0 ? "NoEligibleSource" : operation.ReasonCode,
                Reason = operation.CanAdd && choices.Length == 0 ? "No eligible source or approved choice is available for this payer." : operation.Reason
            };
        }).ToArray()
    };

    private static IEnumerable<CollectionSessionSourceChoice> Choices(CollectionSessionDiscovery d, CollectionSessionCapability operation)
    {
        var code = operation.OperationCode;
        switch (operation.Kind)
        {
            case CollectionSessionItemKind.Obligation:
                foreach (var source in d.ObligationSources ?? [])
                    if (source.CanAddToDraft && source.OutstandingAmount > 0m &&
                        (source.Kind == ObligationKind.KanmanggaySpaceRental ? code == "KANMANGGAY_SPACE_RENTAL" :
                         source.Kind == ObligationKind.FiestaArawLotRental ? code == "FIESTA_ARAW_LOT_RENTAL" : code == "FISH_MEAT_VENDOR_FEE"))
                        yield return new($"{code}|{source.AccountId:N}|{source.PeriodStart:yyyy-MM-dd}", operation.Kind.Value,
                            code, source.PayerName ?? source.SubjectLabel,
                            source.Event is { } rentalEvent ? $"{rentalEvent} · Lot {source.SubjectLabel} · {source.PeriodStart:MMM d, yyyy}" : $"{source.SubjectLabel} · {source.PeriodStart:MMM yyyy}",
                            new(AccountId: source.AccountId, Year: source.PeriodStart.Year, Month: source.PeriodStart.Month,
                                PeriodStart: source.PeriodStart, Event: source.Event),
                            RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance,
                            source.OutstandingAmount, source.OutstandingAmount, RateId: source.RateId, RequiredInputs: ["AmountReceived"]);
                break;
            case CollectionSessionItemKind.Water:
                foreach (var source in d.WaterSources ?? [])
                    if (source.CanCollect || source.CanEnterDirect)
                        yield return new($"{code}|{source.StallId:N}|{source.BillingYear:D4}-{source.BillingMonth:D2}", operation.Kind.Value,
                            code, source.PayerName ?? source.StallNo, $"{source.Section} · Stall {source.StallNo}",
                            new(StallId: source.StallId, UtilityBillId: source.UtilityBillId, SourceVersion: source.WaterSourceVersion,
                                Year: source.BillingYear, Month: source.BillingMonth), RevenueInstrumentType.CashTicket,
                            source.PreparedAmount.HasValue ? CollectionSessionAmountRule.PreparedBalance : CollectionSessionAmountRule.DirectAmount,
                            source.PreparedAmount.HasValue ? source.OutstandingAmount : null,
                            source.PreparedAmount.HasValue ? source.OutstandingAmount : WcfCollectionWorkflow.MaximumDirectCollectionAmount,
                            RequiredInputs: ["AmountReceived"]);
                break;
            case CollectionSessionItemKind.Electricity:
                foreach (var source in d.ElectricitySources ?? [])
                    if (source.CanPostCanonical)
                        yield return new($"{code}|{source.StallId:N}|{source.BillingYear:D4}-{source.BillingMonth:D2}", operation.Kind.Value,
                            code, source.PayerNameSnapshot ?? source.StallNo, $"{source.FacilityName} · Stall {source.StallNo}",
                            new(StallId: source.StallId, UtilityBillId: source.UtilityBillId, SourceVersion: source.ElectricitySourceVersion,
                                Year: source.BillingYear, Month: source.BillingMonth), source.Instrument,
                            source.ChargeBasis == "DirectCollection" ? CollectionSessionAmountRule.DirectAmount : CollectionSessionAmountRule.PreparedBalance,
                            source.ChargeBasis == "DirectCollection" ? null : source.OutstandingAmount,
                            source.ChargeBasis == "DirectCollection" ? null : source.OutstandingAmount, RequiredInputs: ["AmountReceived"]);
                break;
            case CollectionSessionItemKind.VendorFee:
                foreach (var source in d.VendorFeeSources ?? [])
                    if (source.CanCollect)
                        yield return new($"{code}|{source.StallId:N}", operation.Kind.Value, code, source.PayerName,
                            $"{source.Section} · Stall {source.StallNo}", new(StallId: source.StallId, OccupancyId: source.OccupancyId),
                            RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount, RequiredInputs: ["AmountReceived"]);
                break;
            case CollectionSessionItemKind.NpmWholePayment:
                foreach (var source in d.NpmWholeSources ?? [])
                    yield return new($"{code}|{source.StallId:N}|{source.Year:D4}-{source.Month:D2}", operation.Kind.Value, code,
                        source.PayerName, $"New Public Market · Stall {source.StallNo}",
                        new(StallId: source.StallId, OccupancyId: source.OccupancyId, Year: source.Year, Month: source.Month),
                        source.Instrument, CollectionSessionAmountRule.MonthlyRemaining, source.RemainingAmount);
                break;
            case CollectionSessionItemKind.Weighing:
                foreach (var source in d.WeighingSources ?? [])
                    foreach (var rate in d.WeighingRates ?? [])
                        if (rate.RateId.HasValue)
                            yield return new($"{code}|{source.StallId:N}|{(int)rate.Type}", operation.Kind.Value, code, source.PayerName,
                                $"{source.Context} · Stall {source.StallNo} · {rate.Type}",
                                new(StallId: source.StallId, OccupancyId: source.OccupancyId, WeighingType: rate.Type),
                                RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.QuantityRate,
                                Rate: rate.RatePerKilo, RateEffectiveDate: rate.EffectiveDate, RateId: rate.RateId, RequiredInputs: ["Kilograms"]);
                break;
            case CollectionSessionItemKind.Slaughter:
                foreach (var source in d.SlaughterOptions ?? [])
                    yield return new($"{code}|{(int)source.Animal}|{source.CustomAnimalName}", operation.Kind.Value, code, source.Name,
                        "Slaughter transaction", new(Animal: source.Animal, CustomAnimalName: source.CustomAnimalName),
                        source.Instrument ?? RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.QuantityRate,
                        RequiredInputs: d.PayorId.HasValue ? ["Heads"] : ["Heads", "OwnerName"]);
                break;
            case CollectionSessionItemKind.GovernedService:
                foreach (var entry in (d.ServiceTerms ?? []).Where(x => x.Terms.OperationCode == code))
                {
                    var terms = entry.Terms;
                    var required = terms.RequiresReference ? new[] { "Reference" } : Array.Empty<string>();
                    if (terms.Basis == GovernedServiceBasis.VehicleClassRate)
                    {
                        foreach (var vehicle in terms.VehicleClasses ?? [])
                            yield return new($"{code}|{entry.Mode}|{vehicle.Code}", operation.Kind.Value, code, vehicle.Name,
                                terms.Name, new(VehicleClassCode: vehicle.Code, Mode: entry.Mode), terms.Instrument,
                                CollectionSessionAmountRule.FixedAmount, vehicle.Amount, RequiredInputs: required);
                    }
                    else if (terms.Basis == GovernedServiceBasis.ApprovedFeeOption)
                    {
                        foreach (var fee in terms.FeeOptions ?? [])
                            yield return new($"{code}|{entry.Mode}|{fee.Id:N}", operation.Kind.Value, code, fee.Name,
                                fee.Location ?? terms.Name, new(FeeOptionId: fee.Id, Mode: entry.Mode), terms.Instrument,
                                fee.Basis == GovernedServiceBasis.FixedAmount ? CollectionSessionAmountRule.FixedAmount : CollectionSessionAmountRule.DirectAmount,
                                fee.Amount, fee.MaximumAmount, RateEffectiveDate: fee.EffectiveDate, RateId: fee.RateId,
                                RequiredInputs: fee.Basis == GovernedServiceBasis.FixedAmount ? required : required.Append("AmountReceived").ToArray());
                    }
                    else
                        yield return new($"{code}|{entry.Mode}", operation.Kind.Value, code, terms.Name,
                            entry.Mode == GovernedServiceMode.QuickAmount ? "Quick amount" : entry.Mode == GovernedServiceMode.WholePayment ? "Whole payment" : entry.Mode == GovernedServiceMode.DailyTransaction ? "Daily transaction" : terms.Name,
                            new(Mode: entry.Mode), terms.Instrument,
                            terms.Basis == GovernedServiceBasis.FixedAmount ? CollectionSessionAmountRule.FixedAmount : CollectionSessionAmountRule.DirectAmount,
                            terms.FixedAmount, terms.MaximumAmount,
                            RequiredInputs: terms.Basis == GovernedServiceBasis.FixedAmount ? required : required.Append("AmountReceived").ToArray());
                }
                break;
        }
    }
}
