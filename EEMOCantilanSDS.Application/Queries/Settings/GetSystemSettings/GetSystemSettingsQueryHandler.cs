using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;

public class GetSystemSettingsQueryHandler(
    IMunicipalityRepository municipalityRepository,
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

        // LGU identity (name + province) is sourced from the Municipality registry record so the portal
        // reflects the DB rather than hardcoded constants (multi-LGU readiness). The office label, system
        // name and receipt line remain app-level for now. Nothing changes for Cantilan — the record's
        // Name/Province equal the constants — and it falls back to the constants if the registry is empty.
        var lgu = await municipalityRepository.GetDefaultAsync(ct);

        var office = new OfficeProfileDto(
            OfficeProfile.Office,
            string.IsNullOrWhiteSpace(lgu?.Name) ? OfficeProfile.Municipality : lgu!.Name,
            string.IsNullOrWhiteSpace(lgu?.Province) ? OfficeProfile.Province : lgu!.Province,
            OfficeProfile.SystemName,
            OfficeProfile.ReceiptsIssuedBy);

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

        var facilities = BuildFacilities(npmDaily, npmFish);

        var dto = new SystemSettingsDto(office, security, collection, system, facilities);
        return Result<SystemSettingsDto>.Success(dto);
    }

    // Fixed ordinance rates come straight from FeeRates; per-stall monthly rentals are stored per
    // stall (admin-entered) so they are described rather than given a single number.
    private static IReadOnlyList<FacilityRuleDto> BuildFacilities(decimal npmDaily, decimal npmFish) =>
    [
        new("NPM", "New Public Market", "Daily stall",
            $"₱{npmDaily:0}/day + ₱{npmFish:0}/kg fish", "Daily"),
        new("TCC", "Tampak Commercial Center", "Monthly rental", "Per stall contract", "Monthly"),
        new("NCC", "New Commercial Center", "Monthly rental", "Per stall contract", "Monthly"),
        new("BBQ", "Barbecue Stand", "Monthly rental", "Per stall contract", "Monthly"),
        new("ICE", "Iceplant", "Monthly rental", "Per stall contract", "Monthly"),
        new("SLH", "Slaughterhouse", "Per-head · paid on service",
            $"Hog ₱{FeeRates.SlhHogTotalPerHead:0} · Large ₱{FeeRates.SlhLargeTotalPerHead:0}", "Per transaction"),
        new("TRM", "Transport Terminal", "Per-trip · paid on service",
            $"₱{FeeRates.TrmTripFee:0}/trip", "Per trip"),
        new("TPM", "Tabo-an Public Market", "Weekly market",
            $"₱{FeeRates.TpmVendorFee:0}/vendor", "Fridays"),
    ];
}
