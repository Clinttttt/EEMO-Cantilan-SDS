using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
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
    IFeeRateResolver feeRateResolver)
    : IRequestHandler<GetSystemSettingsQuery, Result<SystemSettingsDto>>
{
    private const string TimeZoneLabel = "Philippine Standard Time (UTC+8)";

    public async Task<Result<SystemSettingsDto>> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        // Resolve the current municipality's fixed NPM rates as of today (falls back to the ordinance
        // constants, so Cantilan displays the same ₱30/day + ₱1/kg).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = DateOnly.FromDateTime(PhilippineTime.Now);
        var npmDaily = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);

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
            ServerDate: PhilippineTime.Now);

        // Only the facilities the current tenant actually operates are listed (their fee schedule). Cantilan
        // has all eight seeded, so its list is unchanged; other LGUs show only their configured facilities.
        var tenantCodes = (await facilityRepository.GetFacilityNamesAsync(ct)).Keys.ToHashSet();
        var facilities = BuildFacilities(npmDaily, npmFish, tenantCodes);

        var dto = new SystemSettingsDto(office, security, collection, system, facilities);
        return Result<SystemSettingsDto>.Success(dto);
    }

    // Fixed ordinance rates come straight from FeeRates; per-stall monthly rentals are stored per
    // stall (admin-entered) so they are described rather than given a single number. The catalog is
    // filtered to the tenant's actual facilities (in canonical order).
    private static IReadOnlyList<FacilityRuleDto> BuildFacilities(
        decimal npmDaily, decimal npmFish, IReadOnlySet<FacilityCode> tenantCodes)
    {
        var catalog = new (FacilityCode Code, FacilityRuleDto Dto)[]
        {
            (FacilityCode.NPM, new("NPM", "New Public Market", "Daily stall",
                $"₱{npmDaily:0}/day + ₱{npmFish:0}/kg fish", "Daily")),
            (FacilityCode.TCC, new("TCC", "Tampak Commercial Center", "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.NCC, new("NCC", "New Commercial Center", "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.BBQ, new("BBQ", "Barbecue Stand", "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.ICE, new("ICE", "Iceplant", "Monthly rental", "Per stall contract", "Monthly")),
            (FacilityCode.SLH, new("SLH", "Slaughterhouse", "Per-head · paid on service",
                $"Hog ₱{FeeRates.SlhHogTotalPerHead:0} · Large ₱{FeeRates.SlhLargeTotalPerHead:0}", "Per transaction")),
            (FacilityCode.TRM, new("TRM", "Transport Terminal", "Per-trip · paid on service",
                $"₱{FeeRates.TrmTripFee:0}/trip", "Per trip")),
            (FacilityCode.TPM, new("TPM", "Tabo-an Public Market", "Weekly market",
                $"₱{FeeRates.TpmVendorFee:0}/vendor", "Fridays")),
        };

        return catalog.Where(x => tenantCodes.Contains(x.Code)).Select(x => x.Dto).ToList();
    }
}
