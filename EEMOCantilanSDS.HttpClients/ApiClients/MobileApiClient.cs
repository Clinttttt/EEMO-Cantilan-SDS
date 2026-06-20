using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.HttpClients;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class MobileApiClient(HttpClient http) : HandleResponse(http), IMobileApiClient
{
    public async Task<Result<MobileMenuDto>> GetMenuAsync() =>
        await GetAsync<MobileMenuDto>("api/Mobile/menu");

    public async Task<Result<MobileCollectorProfileDto>> GetProfileAsync() =>
        await GetAsync<MobileCollectorProfileDto>("api/Mobile/profile");

    public async Task<Result<bool>> UpdateProfileAsync(UpdateMobileProfileRequest request) =>
        await PutAsync<UpdateMobileProfileRequest, bool>("api/Mobile/profile", request);

    public async Task<Result<IReadOnlyList<MobileCollectorRecordDto>>> GetRecordsAsync(FacilityCode? facility, DateOnly from, DateOnly to)
    {
        var facilityParam = facility.HasValue ? $"facility={facility}&" : string.Empty;
        return await GetAsync<IReadOnlyList<MobileCollectorRecordDto>>(
            $"api/Mobile/records?{facilityParam}from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
    }

    public async Task<Result<MobileCollectorReportDto>> GetReportAsync(FacilityCode? facility, int year, int month)
    {
        var facilityParam = facility.HasValue ? $"facility={facility}&" : string.Empty;
        return await GetAsync<MobileCollectorReportDto>(
            $"api/Mobile/reports?{facilityParam}year={year}&month={month}");
    }

    public async Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month) =>
        await GetAsync<MobileNpmCollectionDto>($"api/Mobile/npm/collections?year={year}&month={month}");

    public async Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request) =>
        await PostAsync<RecordMobileNpmCollectionRequest, bool>("api/Mobile/npm/collections/record", request);

    public async Task<Result<MobileMonthlyCollectionDto>> GetMonthlyCollectionAsync(FacilityCode facility, int year, int month) =>
        await GetAsync<MobileMonthlyCollectionDto>($"api/Mobile/monthly/collections?facility={facility}&year={year}&month={month}");

    public async Task<Result<bool>> RecordMonthlyCollectionAsync(RecordMobileMonthlyCollectionRequest request) =>
        await PostAsync<RecordMobileMonthlyCollectionRequest, bool>("api/Mobile/monthly/collections/record", request);

    public async Task<Result<MobileSlaughterCollectionDto>> GetSlaughterCollectionAsync(int year, int month, int day) =>
        await GetAsync<MobileSlaughterCollectionDto>($"api/Mobile/slaughter/collections?year={year}&month={month}&day={day}");

    public async Task<Result<bool>> RecordSlaughterAsync(RecordMobileSlaughterRequest request) =>
        await PostAsync<RecordMobileSlaughterRequest, bool>("api/Mobile/slaughter/record", request);

    public async Task<Result<bool>> UpdateSlaughterAsync(UpdateMobileSlaughterRequest request) =>
        await UpdateAsync<UpdateMobileSlaughterRequest, bool>("api/Mobile/slaughter/update", request);

    public async Task<Result<MobileTrmCollectionDto>> GetTrmCollectionAsync() =>
        await GetAsync<MobileTrmCollectionDto>("api/Mobile/trm/collections");

    public async Task<Result<TrmTripDto>> RecordTripAsync(RecordMobileTripRequest request) =>
        await PostAsync<RecordMobileTripRequest, TrmTripDto>("api/Mobile/trm/trips", request);

    public async Task<Result<TrmTransporterDto>> AddTransporterAsync(string name, string organization, string route, string plate) =>
        await PostAsync<object, TrmTransporterDto>("api/trm/transporters",
            new { Name = name, Organization = organization, DefaultRoute = route, PlateNumber = plate, Remarks = (string?)null });

    public async Task<Result<MobileTpmCollectionDto>> GetTpmCollectionAsync() =>
        await GetAsync<MobileTpmCollectionDto>("api/Mobile/tpm/collections");

    public async Task<Result<TpmVendorAttendanceDto>> AddTpmVendorAsync(AddMobileTpmVendorRequest request) =>
        await PostAsync<AddMobileTpmVendorRequest, TpmVendorAttendanceDto>("api/Mobile/tpm/vendors", request);

    public async Task<Result<bool>> MarkTpmVendorPaidAsync(MarkMobileTpmVendorPaidRequest request) =>
        await PostAsync<MarkMobileTpmVendorPaidRequest, bool>("api/Mobile/tpm/attendance/payment", request);

    public async Task<Result<bool>> HideSuggestionAsync(HideMobileSuggestionRequest request) =>
        await PostAsync<HideMobileSuggestionRequest, bool>("api/Mobile/suggestions/hide", request);

    public async Task<Result<SyncOfflineCollectionsResultDto>> SyncOfflineCollectionsAsync(
        EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections.SyncOfflineCollectionsCommand command) =>
        await PostAsync<EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections.SyncOfflineCollectionsCommand,
            SyncOfflineCollectionsResultDto>("api/Mobile/sync", command);

    public async Task<Result<Application.Dtos.Payors.StallActivationCodeDto>> GenerateActivationCodeAsync(
        Application.Command.Payors.GenerateStallActivationCode.GenerateStallActivationCodeCommand command) =>
        await PostAsync<Application.Command.Payors.GenerateStallActivationCode.GenerateStallActivationCodeCommand,
            Application.Dtos.Payors.StallActivationCodeDto>("api/activation-codes/generate", command);

    public async Task<Result<bool>> IssueOnlinePaymentOrNumberAsync(Guid transactionId, string orNumber) =>
        await PostAsync<object, bool>($"api/onlinepayments/{transactionId}/or-number", new { ORNumber = orNumber });
}
