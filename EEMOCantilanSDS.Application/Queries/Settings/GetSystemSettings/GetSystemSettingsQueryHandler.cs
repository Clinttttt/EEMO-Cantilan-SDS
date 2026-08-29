using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;

public class GetSystemSettingsQueryHandler(
    IMunicipalityRepository municipalityRepository,
    IFacilityRepository facilityRepository,
    ITenantContext tenantContext,
    IFeeRateResolver feeRateResolver, IClock clock,
    ITpmMarketDayProvider marketDayProvider)
    : IRequestHandler<GetSystemSettingsQuery, Result<SystemSettingsDto>>
{
    private const string TimeZoneLabel = "Philippine Standard Time (UTC+8)";

    public async Task<Result<SystemSettingsDto>> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        // The fee snapshot resolves each fixed rate to the tenant's own value, falling back to the ordinance
        // constants, so Cantilan is byte-for-byte unchanged.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = DateOnly.FromDateTime(clock.PhilippineNow);

        // LGU identity (office label, name, province, receipt line) is sourced from the CURRENT tenant's
        // Municipality record — resolved the same way as the branding endpoint (JWT tenant claim → identifier)
        // — rather than the default LGU or hardcoded constants, so each municipality sees its own profile.
        // Cantilan is unchanged: its seeded record (OfficeName/Name/Province/OfficeAcronym) equals the
        // OfficeProfile constants, and the whole thing falls back to those constants if the registry is empty.
        var lgu = await municipalityRepository.GetByIdentifierAsync(tenantContext.TenantCode, ct)
                  ?? await municipalityRepository.GetDefaultAsync(ct);

        var officeLabel = string.IsNullOrWhiteSpace(lgu?.OfficeName) ? OfficeProfile.Office : lgu!.OfficeName;
        var municipality = string.IsNullOrWhiteSpace(lgu?.Name) ? OfficeProfile.Municipality : lgu!.Name;
        var province = string.IsNullOrWhiteSpace(lgu?.Province) ? OfficeProfile.Province : lgu!.Province;
        var acronym = !string.IsNullOrWhiteSpace(lgu?.OfficeAcronym) ? lgu!.OfficeAcronym!
            : !string.IsNullOrWhiteSpace(lgu?.OfficeName) ? lgu!.OfficeName
            : OfficeProfile.Office;
        var receiptsIssuedBy = lgu is null
            ? OfficeProfile.ReceiptsIssuedBy
            : $"{acronym} · Municipality of {municipality}";

        var office = new OfficeProfileDto(
            officeLabel,
            municipality,
            province,
            OfficeProfile.SystemName,
            receiptsIssuedBy);

        var security = new SecurityPolicyDto(
            DomainRules.AccessTokenMinutes,
            DomainRules.RefreshTokenDays,
            DomainRules.MaxFailedLoginAttempts,
            DomainRules.LockoutMinutes,
            new[] { "Head", "Admin", "Collector", "Payor" });

        var collection = new CollectionRulesDto(
            DelinquentThresholdMonths: DomainRules.DelinquentThresholdMonths,
            ArrearsMinMonths: 1,
            ArrearsMaxMonths: DomainRules.DelinquentThresholdMonths - 1,
            DelinquencyWindowMonths: DomainRules.PaymentHistoryMonths,
            ContractExpiryWarningMonths: DomainRules.ExpiringSoonMonths,
            TimeZone: TimeZoneLabel);

        var system = new SystemInfoDto(
            ApplicationName: AppInfo.Name,
            Version: AppInfo.Version,
            Environment: string.IsNullOrWhiteSpace(request.Environment) ? "Unknown" : request.Environment,
            TimeZone: TimeZoneLabel,
            ServerDate: clock.PhilippineNow);

        // Only the facilities the current tenant operates are listed — using the tenant's own facility
        // names and resolved rates. Cantilan has all eight seeded (names/rates equal the constants), so its
        // list is unchanged; other LGUs show their own facilities, names, rates, and market day.
        var facilityNames = await facilityRepository.GetFacilityNamesAsync(ct);

        // The market's own areas and sections, so the settings page can state a rate the office has priced APART from the
        // market's. Read here rather than derived: an area's label is the office's own word for it, and a section of its
        // own has no key to look up. Nothing is appended for an office that prices nothing apart, which is every office
        // today, so its line is unchanged.
        var npmFacility = await facilityRepository.GetByCodeAsync(FacilityCode.NPM, ct);
        var npmSections = await facilityRepository.GetNpmCustomSectionsAsync(ct);

        // Asked of the schedule rather than read off the registry column. A move the office scheduled for a later
        // week updates that column only when it starts, so reading the column here would have gone on stating the
        // old day after the new one had taken effect — this screen contradicting the collections being taken.
        var marketDay = await marketDayProvider.GetMarketDayAsync(asOf, ct);
        var facilities = BuildFacilities(rateSnapshot, asOf, facilityNames, marketDay, npmFacility, npmSections);

        var dto = new SystemSettingsDto(office, security, collection, system, facilities);
        return Result<SystemSettingsDto>.Success(dto);
    }

    // Each facility is listed with the TENANT's own name (fallback to the canonical label) and its
    // resolved fixed rate (fallback to the ordinance constants). Filtered to the tenant's actual facilities
    // in canonical order. Cantilan's names/rates equal the constants, so it is byte-for-byte unchanged.
    private static IReadOnlyList<FacilityRuleDto> BuildFacilities(
        FeeRateSnapshot rates, DateOnly asOf, IReadOnlyDictionary<FacilityCode, string> names, DayOfWeek marketDay,
        Facility? npm = null, IReadOnlyList<NpmCustomSectionDto>? npmSections = null)
    {
        var npmDaily = rates.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var npmFish = rates.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
        var npmMonthlyStated = rates.Resolve(FeeRateKey.NpmMonthlyStall, asOf);
        // The rent a market space is let for: the office's own stated month, or thirty of its daily fee until it
        // states one. The rule the settings page prints has to name it — the daily fee is the installment the
        // month is collected in, and a page that showed only the installment described half the rule.
        var npmMonthly = npmMonthlyStated > 0m
            ? npmMonthlyStated
            : npmDaily * DomainRules.DailyBilledMonthDays;
        var slhHog = rates.Resolve(FeeRateKey.SlhHogPerHead, asOf);
        var slhLarge = rates.Resolve(FeeRateKey.SlhLargePerHead, asOf);
        var trmTrip = rates.Resolve(FeeRateKey.TrmPerTrip, asOf);
        var tpmVendor = rates.Resolve(FeeRateKey.TpmVendorDay, asOf);

        string Name(FacilityCode c, string fallback) =>
            names.TryGetValue(c, out var n) && !string.IsNullOrWhiteSpace(n) ? n : fallback;

        // What the office prices APART from its market: an area of the three, named as the office names it, and a section
        // of its own. Stated after the market's own line, because the market's rate is what a stall is billed wherever the
        // office has priced nothing apart — and nothing at all is added where it has, which is every office today.
        var apart = new List<string>();
        if (npm is not null)
        {
            foreach (var section in Enum.GetValues<MarketSection>())
            {
                if (FacilityRateKeys.PerAreaDailyKey(section) is not { } key) continue;
                if (rates.ResolveOrNull(key, asOf) is not { } areaRate || areaRate <= 0m) continue;

                // The office's own word for the area, or the platform's where it has named none.
                apart.Add($"{npm.SectionLabel(section)} ₱{areaRate:0}/day");
            }
        }

        foreach (var section in npmSections ?? Array.Empty<NpmCustomSectionDto>())
        {
            if (section.DailyRate is { } sectionRate && sectionRate > 0m)
                apart.Add($"{section.Name} ₱{sectionRate:0}/day");
        }

        var npmApart = apart.Count > 0 ? " · " + string.Join(" · ", apart) : string.Empty;

        var catalog = new (FacilityCode Code, FacilityRuleDto Dto)[]
        {
            (FacilityCode.NPM, new("NPM", Name(FacilityCode.NPM, "New Public Market"), "Daily stall",
                npmFish > 0m
                    ? $"₱{npmMonthly:N0}/month, collected at ₱{npmDaily:0}/day + ₱{npmFish:0}/kg fish{npmApart}"
                    : $"₱{npmMonthly:N0}/month, collected at ₱{npmDaily:0}/day{npmApart}", "Daily")),
            (FacilityCode.TCC, new("TCC", Name(FacilityCode.TCC, "Tampak Commercial Center"), "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.NCC, new("NCC", Name(FacilityCode.NCC, "New Commercial Center"), "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.BBQ, new("BBQ", Name(FacilityCode.BBQ, "Barbecue Stand"), "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.ICE, new("ICE", Name(FacilityCode.ICE, "Iceplant"), "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.SLH, new("SLH", Name(FacilityCode.SLH, "Slaughterhouse"), "Per-head · paid on service",
                $"Hog ₱{slhHog:0} · Large ₱{slhLarge:0}", "Per transaction")),
            (FacilityCode.TRM, new("TRM", Name(FacilityCode.TRM, "Transport Terminal"), "Per-trip · paid on service",
                $"₱{trmTrip:0}/trip", "Per trip")),
            (FacilityCode.TPM, new("TPM", Name(FacilityCode.TPM, "Tabo-an Public Market"), "Weekly market",
                $"₱{tpmVendor:0}/vendor", $"{marketDay}s")),
        };

        var rows = catalog.Where(x => names.ContainsKey(x.Code)).Select(x => x.Dto).ToList();

        // Head-added custom facilities (Custom1..Custom5) are monthly-rental. List them data-driven from the
        // tenant's own facilities, after the canonical ones, in slot order. Code = the enum name so the client
        // resolves the tenant's acronym via ShortNameOf; Name is the tenant's own facility name. Cantilan has
        // no custom facilities, so its list is byte-for-byte unchanged.
        foreach (var custom in names
            .Where(n => (int)n.Key >= (int)FacilityCode.Custom1)
            .OrderBy(n => (int)n.Key))
        {
            rows.Add(new FacilityRuleDto(
                custom.Key.ToString(),
                string.IsNullOrWhiteSpace(custom.Value) ? custom.Key.ToString() : custom.Value,
                "Monthly rental",
                "Per stall contract",
                "Monthly"));
        }

        return rows;
    }
}