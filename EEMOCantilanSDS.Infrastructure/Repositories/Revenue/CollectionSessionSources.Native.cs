using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
public sealed partial class CollectionSessionSources
{
    private OfficeCollectionWorkflow Office => new(db, user, municipality, clock, new FeeRateResolver(db));
    public async Task<bool> SourceExistsAsync(CollectionSourceIdentity identity, CancellationToken ct) => identity.Kind switch {
        SourceIdentityKind.Occupancy => await db.Stalls.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Contracts.Any(c => c.Id == identity.Id), ct),
        SourceIdentityKind.SpaceAccount => await db.ObligationAccounts.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == identity.Id && (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.FiestaArawLotRental), ct),
        SourceIdentityKind.FishMeatVendorRegistration => await db.FishMeatVendorRegistrations.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == identity.Id, ct),
        _ => false };
    public async Task<IReadOnlyList<CollectionSourceSearchResult>> SearchSourcesAsync(string? search, CancellationToken ct)
    {
        var term = (search ?? "").Trim().ToLowerInvariant(); var today = clock.PhilippineToday;
        var results = new List<CollectionSourceSearchResult>();
        var facilities = await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive)
            .SelectMany(x => x.FacilityAssignments.Select(a => a.FacilityCode)).ToListAsync(ct);
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
            .Where(x => x.MunicipalityId == Tenant && facilities.Contains(x.Facility!.Code) && x.Contracts.Any(c => c.ActualOccupant != null && c.ActualOccupant.ToLower().Contains(term)))
            .OrderBy(x => x.StallNo).ThenBy(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var stall in stalls)
        {
            var occupancy = stall.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract;
            if (occupancy?.ActualOccupant is { } name && name.ToLowerInvariant().Contains(term))
                results.Add(new(new(SourceIdentityKind.Occupancy, occupancy.Id), name, $"{stall.Facility!.Name} · Stall {stall.StallNo}", "FACILITY_" + stall.Facility.Code));
        }
        var assigned = await db.CollectorOperationAssignments.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.CollectorId == user.CollectorId).Select(x => x.OperationCode).ToListAsync(ct);
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Kind != ObligationKind.FishMeatVendorFee && x.ActiveFrom <= today &&
            (x.ActiveTo == null || x.ActiveTo >= today) &&
            (x.SubjectLabel.ToLower().Contains(term) || x.ActualOccupant != null && x.ActualOccupant.ToLower().Contains(term) || db.Payors.Any(p => p.MunicipalityId == Tenant && p.Id == x.PayorId && p.DisplayName.ToLower().Contains(term))) &&
            (x.Kind == ObligationKind.KanmanggaySpaceRental && assigned.Contains(CollectorOperationCodes.KanmanggaySpaceRental) ||
                x.Kind == ObligationKind.FiestaArawLotRental && assigned.Contains(CollectorOperationCodes.FiestaArawLotRental))).OrderBy(x => x.SubjectLabel).ThenBy(x => x.Id).Take(50).ToListAsync(ct);
        // Historical master links supply display compatibility only. Source account identity, never name/Payor, authorizes collection.
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, accounts, today, ct);
        foreach (var account in accounts)
        {
            var name = quotes.FirstOrDefault(x => x.AccountId == account.Id)?.PayerName ?? account.SubjectLabel;
            if (name.ToLowerInvariant().Contains(term) || account.SubjectLabel.ToLowerInvariant().Contains(term))
                results.Add(new(new(SourceIdentityKind.SpaceAccount, account.Id), name,
                    account.Kind == ObligationKind.KanmanggaySpaceRental ? $"Kanmanggay · Space {account.SubjectLabel}" : $"Fiesta / Araw · Lot {account.SubjectLabel}",
                    account.Kind == ObligationKind.KanmanggaySpaceRental ? CollectorOperationCodes.KanmanggaySpaceRental : CollectorOperationCodes.FiestaArawLotRental));
        }
        if (assigned.Contains(CollectorOperationCodes.FishMeatVendorFee) || assigned.Contains(CollectorOperationCodes.WeightAndMeasure))
            results.AddRange(await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.TaxYear == today.Year && x.Status == VendorRegistrationStatus.Active && x.DisplayName.ToLower().Contains(term))
                .OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Take(50).Select(x => new CollectionSourceSearchResult(new(SourceIdentityKind.FishMeatVendorRegistration, x.Id),
                    x.DisplayName, x.VendorType.ToString() + " · " + x.TaxYear, CollectorOperationCodes.FishMeatVendorFee, x.TaxYear, x.VendorType)).ToListAsync(ct));
        return results.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Identity.Kind).ThenBy(x => x.Identity.Id).Take(50).ToArray();
    }
    public async Task<CollectionSessionDiscovery> DiscoverNativeAsync(CollectionSourceIdentity? identity, DateOnly date, CancellationToken ct)
    {
        var rows = new List<CollectionSessionCapability>();
        async Task AddOffice(string operation, SourceNativeChargeIntent charge, string display, string context, CollectionSessionAmountRule rule, CollectionFamily family)
        {
            var q = await Office.QuoteAsync(new(Guid.Empty, date, 1m, charge, "quote"), ct);
            if (!q.IsSuccess) return;
            var choices = new[] { new CollectionSessionSourceChoice(operation + "|" + charge.VendorRegistrationId + "|" + charge.Section + "|" + charge.VehicleClassId,
                CollectionSessionItemKind.SourceNative, operation, display, context,
                new(VendorRegistrationId: charge.VendorRegistrationId, TerminalSection: charge.Section, VehicleClassId: charge.VehicleClassId), q.Value!.Instrument, rule,
                Rate: q.Value.Rate, RateId: q.Value.RateId, RateEffectiveDate: q.Value.RateEffectiveDate,
                RequiredInputs: charge.Kilograms.HasValue ? ["Kilograms"] : ["AmountReceived"]) };
            rows.Add(new(CollectionSessionItemKind.SourceNative, operation, display, true, true, null, null, false, [], true, choices, family));
        }
        if (identity?.Kind == SourceIdentityKind.FishMeatVendorRegistration)
        {
            var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == identity.Id && x.TaxYear == date.Year, ct);
            if (vendor is { Status: VendorRegistrationStatus.Active })
            {
                await AddOffice(CollectorOperationCodes.FishMeatVendorFee, new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id), "Fish / Meat Vendor Fee", vendor.DisplayName, CollectionSessionAmountRule.DirectAmount, CollectionFamily.Market);
                await AddOffice(CollectorOperationCodes.WeightAndMeasure, new(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 1m), "Weight & Measure", vendor.VendorType.ToString(), CollectionSessionAmountRule.QuantityRate, CollectionFamily.Market);
            }
        }
        else if (identity is null)
            await AddOffice(CollectorOperationCodes.FishMeatVendorFee, new(CollectorOperationCodes.FishMeatVendorFee), "Fish / Meat Vendor Fee", "Payer Snapshot", CollectionSessionAmountRule.DirectAmount, CollectionFamily.Market);
        else if (identity.Kind == SourceIdentityKind.SpaceAccount)
        {
            var account = await db.ObligationAccounts.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == identity.Id, ct);
            var code = account.Kind == ObligationKind.KanmanggaySpaceRental ? CollectorOperationCodes.KanmanggaySpaceRental : CollectorOperationCodes.FiestaArawLotRental;
            if (await db.CollectorOperationAssignments.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectorId == user.CollectorId && x.OperationCode == code, ct))
            {
                var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, [account], date, ct);
                var d = CollectionSessionChoiceProjection.Apply(new(null, date,
                    [new(CollectionSessionItemKind.Obligation, code, ObligationCollectionSource.KindLabel(account.Kind), true, true, null, null, false, [], Family: CollectionFamily.Space)], ObligationSources: quotes));
                rows.AddRange(d.Operations.Where(x => x.EligibleChoiceCount > 0));
            }
        }
        else if (identity.Kind == SourceIdentityKind.Occupancy)
        {
            var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts).SingleAsync(x => x.MunicipalityId == Tenant && x.Contracts.Any(c => c.Id == identity.Id), ct);
            var owner = stall.OccupancyAnsweringForMonth(date.Year, date.Month, date)?.Contract;
            if (owner?.Id == identity.Id)
            {
                var water = await Water.GetMobileSourcesAsync(date.Year, date.Month, ct, stall.Id, date);
                var electric = await Composer.GetMobileEcfSourcesAsync(ct: ct);
                var operations = new List<CollectionSessionCapability>();
                if (npmDaily is not null && stall.Facility!.Code == FacilityCode.NPM)
                {
                    var daily = await npmDaily.PreviewAsync(new(Guid.NewGuid(), date, [new(Guid.NewGuid(), stall.Id)]), ct);
                    if (daily.IsSuccess && daily.Value!.CanRecord)
                    {
                        var source = daily.Value.Sources.Single();
                        var instrument = await DailyPoster.GetInstrumentAsync(date, ct);
                        if (instrument.HasValue) rows.Add(new(CollectionSessionItemKind.NpmDaily, CollectorOperationCodes.NpmDaily,
                            "Daily stall payment", true, true, null, null, false, [], true,
                            [new($"Daily|{stall.Id:N}|{date:yyyy-MM-dd}", CollectionSessionItemKind.NpmDaily, CollectorOperationCodes.NpmDaily,
                                "Daily stall payment", $"Stall {stall.StallNo} · {date:MMM d, yyyy}",
                                new(StallId: stall.Id, OccupancyId: owner.Id, PeriodStart: date), instrument.Value,
                                CollectionSessionAmountRule.FixedAmount, source.EffectiveCharge, RequiredInputs: [])], CollectionFamily.Rent));
                    }
                }
                if (water.IsSuccess) operations.Add(new(CollectionSessionItemKind.Water, "WCF", "Water", true, true, null, null, false, [], Family: CollectionFamily.Market));
                if (electric.IsSuccess) operations.Add(new(CollectionSessionItemKind.Electricity, "ECF", "Electricity", true, true, null, null, false, [], Family: CollectionFamily.Market));
                var whole = npmWhole is not null && stall.Facility!.Code == FacilityCode.NPM ? await npmWhole.QuoteAsync(stall.Id, date.Year, date.Month, ct, date) : null;
                var wholeSources = new List<SessionNpmWholeSource>();
                if (whole?.IsSuccess == true && whole.Value!.Amount > 0m)
                {
                    operations.Add(new(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", true, true, null, null, false, [], Family: CollectionFamily.Rent));
                    wholeSources.Add(new(stall.Id, owner.Id, null, stall.StallNo, owner.ActualOccupant ?? "Occupant", date.Year, date.Month, whole.Value.Instrument, whole.Value.Amount, whole.Value.MonthlyObligation, whole.Value.Collected, whole.Value.Credits));
                }
                var d = CollectionSessionChoiceProjection.Apply(new(null, date, operations, water.Value,
                    electric.Value?.Where(x => x.StallId == stall.Id).ToArray(), NpmWholeSources: wholeSources));
                rows.AddRange(d.Operations.Where(x => x.EligibleChoiceCount > 0));
                if (MonthlyRentalFacilities.Codes.Contains(stall.Facility!.Code))
                {
                    var periods = await db.PaymentRecords.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.StallId == stall.Id
                        && x.SettlementAuthorityState == SettlementAuthority.Canonical).OrderBy(x => x.BillingYear).ThenBy(x => x.BillingMonth)
                        .Select(x => new { x.BillingYear, x.BillingMonth }).ToListAsync(ct);
                    var choices = new List<CollectionSessionSourceChoice>();
                    foreach (var period in periods)
                    {
                        try
                        {
                            var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(Tenant, stall.Id, period.BillingYear, period.BillingMonth,
                                date, false, false, "quote", ct);
                            if (facts?.Quote.CanPostCanonical != true || facts.Contract.Id != owner.Id) continue;
                            var q = facts.Quote;
                            choices.Add(new($"Rent|{stall.Id:N}|{period.BillingYear}|{period.BillingMonth}", CollectionSessionItemKind.MonthlyRent,
                                "FACILITY_" + stall.Facility.Code, stall.Facility.Name, $"Stall {stall.StallNo} · {period.BillingYear}-{period.BillingMonth:00}",
                                new(StallId: stall.Id, OccupancyId: owner.Id, Year: period.BillingYear, Month: period.BillingMonth, SourceVersion: q.SourceVersion),
                                q.Instrument, CollectionSessionAmountRule.PreparedBalance, q.OutstandingAmount, q.OutstandingAmount, RequiredInputs: ["AmountReceived"]));
                        }
                        catch (InvalidOperationException) { /* One malformed period cannot hide unrelated eligible sources. */ }
                    }
                    if (choices.Count > 0) rows.Add(new(CollectionSessionItemKind.MonthlyRent, "FACILITY_" + stall.Facility.Code,
                        stall.Facility.Name, true, true, null, null, false, [], true, choices, CollectionFamily.Rent));
                }
            }
        }
        // Payer-optional services are offered only to a direct session (no Source Identity); a selected source never carries them.
        if (identity is null)
        {
        foreach (var section in Enum.GetValues<TerminalSection>()) await AddOffice(CollectorOperationCodes.Terminal, new(CollectorOperationCodes.Terminal, Section: section),
            OfficeCollectionWorkflow.SectionName(section), "Income From Terminal", CollectionSessionAmountRule.DirectAmount, CollectionFamily.Terminal);
        foreach (var vehicle in await Office.VehicleChoicesAsync(date, ct))
            await AddOffice(CollectorOperationCodes.Terminal, new(CollectorOperationCodes.Terminal, Section: vehicle.Section, VehicleClassId: vehicle.VehicleClassId),
                vehicle.DisplayName, OfficeCollectionWorkflow.SectionName(vehicle.Section), CollectionSessionAmountRule.DirectAmount, CollectionFamily.Terminal);
        if (sender is not null)
        {
            var optional = await DiscoverAsync(null, date, ct);
            rows.AddRange(optional.Operations.Where(x => x.CanAdd && x.Kind is CollectionSessionItemKind.GovernedService or CollectionSessionItemKind.Slaughter)
                .Select(x => x with { RequiresPayor = false, Family = x.Kind == CollectionSessionItemKind.Slaughter ? CollectionFamily.Slaughterhouse
                    : x.OperationCode == CollectorOperationCodes.VegetableFruitSpaceRental ? CollectionFamily.Space : CollectionFamily.Market }));
        }
        }
        return new(null, date, rows.GroupBy(x => x.OperationCode).Select(g => g.First() with {
            DisplayName = g.Key == CollectorOperationCodes.Terminal ? "Income From Terminal" : g.First().DisplayName,
            Choices = g.SelectMany(x => x.Choices ?? []).OrderBy(x => x.SelectionKey, StringComparer.Ordinal).ToArray()
        }).ToArray(), SourceIdentity: identity);
    }
    private async Task<bool> NativeItemMatchesAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        var identity = session.SourceIdentity!;
        if (item.Kind is CollectionSessionItemKind.GovernedService or CollectionSessionItemKind.Slaughter) return false; // Payer-optional services belong to a direct (source-less) session only.
        if (identity.Kind == SourceIdentityKind.SpaceAccount) return item.Obligation?.AccountId == identity.Id;
        if (identity.Kind != SourceIdentityKind.Occupancy) return false;
        var stallId = item.Water?.StallId ?? item.NpmWhole?.StallId ?? item.NpmDaily?.StallId ?? item.Electricity?.StallId ?? item.Rent?.StallId;
        if (!stallId.HasValue && item.Electricity is { } e)
            stallId = await db.UtilityBills.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == e.UtilityBillId).Select(x => (Guid?)x.StallId).SingleOrDefaultAsync(ct);
        if (!stallId.HasValue) return false;
        var stall = await db.Stalls.AsNoTracking().Include(x => x.Contracts).SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
        var year = item.Water?.Year ?? item.NpmWhole?.Year ?? item.Electricity?.Year ?? item.Rent?.Year ?? session.BusinessDate.Year;
        var month = item.Water?.Month ?? item.NpmWhole?.Month ?? item.Electricity?.Month ?? item.Rent?.Month ?? session.BusinessDate.Month;
        return year is >= 2000 and <= 2200 && month is >= 1 and <= 12 &&
            stall?.OccupancyAnsweringForMonth(year, month, session.BusinessDate)?.Contract?.Id == identity.Id;
    }
    private SourceNativeCollectionRequest NativeRequest(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation) =>
        new(operation, session.BusinessDate, item.ConfirmedAmount, item.Native!, session.PayerSnapshot);
    private NpmDailyCanonicalPoster DailyPoster => new(db, user, municipality, new GovernedCanonicalAuthority(db, municipality));
    private async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteNativeDailyAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        if (npmDaily is null || item.Kind != CollectionSessionItemKind.NpmDaily || session.SourceIdentity?.Kind != SourceIdentityKind.Occupancy)
            return (null, new(item.ClientItemId, "InvalidIntent", "Select today's stall payment."));
        var result = await npmDaily.PreviewAsync(new(session.ClientCollectionSessionId, session.BusinessDate, [new(item.ClientItemId, item.NpmDaily!.StallId)]), ct);
        if (!result.IsSuccess) return (null, new(item.ClientItemId, "SourceNotAvailable", "Today's payment is unavailable."));
        var q = result.Value!;
        if (!q.CanRecord) return (null, q.Problems.FirstOrDefault() ?? new(item.ClientItemId, "SourceNotAvailable", "Today's payment is unavailable."));
        if (item.ConfirmedAmount != q.Total) return (null, new(item.ClientItemId, "AmountChanged", "Review today's stall payment."));
        var instrument = await DailyPoster.GetInstrumentAsync(session.BusinessDate, ct);
        return (new(item.ClientItemId, item.Kind, CollectorOperationCodes.NpmDaily, "Daily stall payment",
            $"Stall {q.Sources.Single().StallNumber} · {session.BusinessDate:MMM d, yyyy}", instrument!.Value, q.Total, q.QuoteFingerprint!, Guid.Empty), null);
    }
    private async Task<CollectionSessionCollection> PostNativeDailyAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        var result = await sender.Send(new EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection.RecordDailyCollectionCommand(
            item.NpmDaily!.StallId, session.BusinessDate, true, ClientOperationId: operation), ct);
        var posted = result.IsSuccess ? await DailyPoster.FindPostedAsync(operation, ct) : null;
        if (posted is null || posted.Amount != item.ConfirmedAmount) throw new CollectionSessionPostingException(item.ClientItemId, "Review today's stall payment.");
        return new(posted.CollectionId, posted.ReferenceCode, (await DailyPoster.GetInstrumentAsync(session.BusinessDate, ct))!.Value,
            posted.Amount, [item.ClientItemId], "Posted");
    }
    private async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteNativeRentAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        var r = item.Rent!;
        if (item.Kind != CollectionSessionItemKind.MonthlyRent || session.SourceIdentity?.Kind != SourceIdentityKind.Occupancy ||
            r.Year is < 2000 or > 2200 || r.Month is < 1 or > 12) return (null, new(item.ClientItemId, "InvalidIntent", "Select a canonical rental period."));
        var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(Tenant, r.StallId, r.Year, r.Month, session.BusinessDate, false, false, "quote", ct);
        if (facts?.Quote.CanPostCanonical != true || facts.Contract.Id != session.SourceIdentity.Id ||
            !await db.CollectorUsers.AnyAsync(x => x.Id == user.CollectorId && x.MunicipalityId == Tenant && x.IsActive && x.FacilityAssignments.Any(a => a.FacilityCode == facts.Stall.Facility!.Code), ct))
            return (null, new(item.ClientItemId, "SourceNotAvailable", "This canonical rental source is unavailable."));
        var q = facts.Quote;
        if (r.SourceVersion != q.SourceVersion || item.ConfirmedAmount > q.OutstandingAmount) return (null, new(item.ClientItemId, "BalanceChanged", "Refresh the rental balance."));
        return (new(item.ClientItemId, item.Kind, "FACILITY_" + facts.Stall.Facility!.Code, facts.Stall.Facility.Name,
            $"Stall {facts.Stall.StallNo} · {r.Year}-{r.Month:00}", q.Instrument, item.ConfirmedAmount, facts.SnapshotJson, Guid.Empty), null);
    }
    private async Task<CollectionSessionCollection> PostNativeRentAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        var r = item.Rent!;
        var result = await Composer.PostMobileRentAsync(new(operation, r.StallId, r.Year, r.Month, item.ConfirmedAmount, r.SourceVersion, session.BusinessDate), ct);
        if (!result.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "Review the rental source.");
        var outcome = result.Value!;
        var line = await db.CollectionLines.AsNoTracking().SingleAsync(x => x.CollectionId == outcome.CollectionId, ct);
        var instrument = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.Id == line.RevenueClassificationPolicyId && x.MunicipalityId == Tenant)
            .Select(x => x.PermittedInstrumentType).SingleAsync(ct);
        return new(outcome.CollectionId, outcome.ReferenceCode, instrument!.Value, outcome.Amount, [item.ClientItemId], outcome.CurrentDisposition);
    }
    private async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteNativeChargeAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        var native = item.Native!;
        // Terminal (like every payer-optional service) belongs to a direct session only; a selected source never carries it.
        if (session.SourceIdentity is not null && native.OperationCode == CollectorOperationCodes.Terminal)
            return (null, new(item.ClientItemId, "PayerMismatch", "Terminal is collected as a direct collection, not under a selected source."));
        if (native.OperationCode is CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure)
        {
            // A registration is a source identity, never an inferred relation from a selected occupancy or matching name.
            if (session.SourceIdentity is { Kind: SourceIdentityKind.FishMeatVendorRegistration } registrationIdentity
                && native.VendorRegistrationId != registrationIdentity.Id)
                return (null, new(item.ClientItemId, "PayerMismatch", "Choose the selected vendor registration."));
            if (native.VendorRegistrationId.HasValue && session.SourceIdentity?.Kind != SourceIdentityKind.FishMeatVendorRegistration)
                return (null, new(item.ClientItemId, "RequiresSourceIdentity", "Select the registered vendor."));
            if (!native.VendorRegistrationId.HasValue && session.SourceIdentity is not null)
                return (null, new(item.ClientItemId, "RequiresPayerSnapshot", "Record an unregistered vendor fee as a walk-up payer."));
        }
        if (item.Kind != CollectionSessionItemKind.SourceNative || session.PayorId.HasValue || item.Native!.VendorRegistrationId.HasValue && session.SourceIdentity is { } identity &&
            (identity.Kind != SourceIdentityKind.FishMeatVendorRegistration || item.Native.VendorRegistrationId != identity.Id))
            return (null, new(item.ClientItemId, "PayerMismatch", "Choose this Source Identity's eligible item."));
        if (session.SourceIdentity is null && item.Native!.VendorRegistrationId.HasValue)
            return (null, new(item.ClientItemId, "RequiresSourceIdentity", "Select the registered vendor."));
        var q = await Office.QuoteAsync(NativeRequest(session, item, item.ClientItemId), ct);
        if (!q.IsSuccess) return (null, new(item.ClientItemId, q.Error!, "Review this collection item."));
        var f = q.Value!;
        if (f.Amount != item.ConfirmedAmount) return (null, new(item.ClientItemId, "AmountChanged", "Review the server amount."));
        return (new(item.ClientItemId, item.Kind, f.OperationCode, f.DisplayName, f.Context, f.Instrument, f.Amount, f.SourceVersion, Guid.Empty), null);
    }
    private async Task<CollectionSessionCollection> PostNativeChargeAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        var result = await Office.PostAsync(NativeRequest(session, item, operation), ct);
        if (!result.IsSuccess) throw new CollectionSessionPostingException(item.ClientItemId, "Review the changed collection source.");
        var c = result.Value!; return new(c.CollectionId, c.ReferenceCode, c.Instrument, c.Amount, [item.ClientItemId], c.Disposition);
    }
}
