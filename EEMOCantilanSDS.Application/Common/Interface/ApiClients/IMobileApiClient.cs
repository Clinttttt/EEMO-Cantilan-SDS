using EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Requests.Mobile;using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMobileApiClient
{
    Task<Result<MobileMenuDto>> GetMenuAsync();
    Task<Result<IReadOnlyList<MobileCollectorRecordDto>>> GetRecordsAsync(FacilityCode? facility, DateOnly from, DateOnly to);
    Task<Result<MobileCollectorReportDto>> GetReportAsync(FacilityCode? facility, int year, int month);
    Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month);
    Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request);
    Task<Result<MobileMonthlyCollectionDto>> GetMonthlyCollectionAsync(FacilityCode facility, int year, int month);
    Task<Result<bool>> RecordMonthlyCollectionAsync(RecordMobileMonthlyCollectionRequest request);
    Task<Result<MobileSlaughterCollectionDto>> GetSlaughterCollectionAsync(int year, int month, int day);
    Task<Result<bool>> RecordSlaughterAsync(RecordMobileSlaughterRequest request);
    Task<Result<bool>> UpdateSlaughterAsync(UpdateMobileSlaughterRequest request);
    Task<Result<MobileTrmCollectionDto>> GetTrmCollectionAsync();
    Task<Result<TrmTripDto>> RecordTripAsync(RecordMobileTripRequest request);
    Task<Result<TrmTransporterDto>> AddTransporterAsync(string name, string organization, string route, string plate);
    Task<Result<MobileTpmCollectionDto>> GetTpmCollectionAsync();
    Task<Result<TpmVendorAttendanceDto>> AddTpmVendorAsync(AddMobileTpmVendorRequest request);
    Task<Result<bool>> MarkTpmVendorPaidAsync(MarkMobileTpmVendorPaidRequest request);
    Task<Result<bool>> HideSuggestionAsync(HideMobileSuggestionRequest request);

    /// <summary>Issues a single-use payor activation code for a stall (collector-facility guarded server-side).</summary>
    Task<Result<StallActivationCodeDto>> GenerateActivationCodeAsync(GenerateStallActivationCodeCommand command);

    /// <summary>Encodes the manual OR for an online payment awaiting OR (preserves online attribution; completes the transaction).</summary>
    Task<Result<bool>> IssueOnlinePaymentOrNumberAsync(Guid transactionId, string orNumber);
}
