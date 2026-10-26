using EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMobileApiClient
{
    Task<Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentAccountsPage>> GetNpmOutstandingAccountsPageAsync(int offset, int pageSize) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentAccountsPage>.Failure("Outstanding rent is unavailable."));
    Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentAccount>>> GetNpmOutstandingAccountsAsync() =>
        Task.FromResult(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentAccount>>.Failure("Outstanding rent is unavailable."));
    Task<Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentQuote>> GetNpmOutstandingQuoteAsync(Guid stallId, Guid? contractId = null, decimal? amount = null) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentQuote>.Failure("Outstanding rent is unavailable."));
    Task<Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto>> RecordNpmOutstandingRentAsync(EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmOutstandingRentPayment request) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto>.Failure("Outstanding rent collection is unavailable."));
    Task<Result<NpmRegistrationRequestDto>> SubmitNpmRequestAsync(SubmitNpmRegistrationRequest request) => Task.FromResult(Result<NpmRegistrationRequestDto>.Failure("Registration requests unavailable."));
    Task<Result<NpmStallNumberSuggestionDto>> SuggestNpmStallAsync(EEMOCantilanSDS.Domain.Enums.MarketSection section) =>
        Task.FromResult(Result<NpmStallNumberSuggestionDto>.Failure("Stall suggestion unavailable."));
    Task<Result<NpmRegistrationRequestDto>> CancelNpmRequestAsync(Guid requestId, CancelNpmRegistrationRequest request) =>
        Task.FromResult(Result<NpmRegistrationRequestDto>.Failure("Cancellation is unavailable."));
    Task<Result<NpmRegistrationRequestDto>> GetPendingNpmRequestAsync(Guid vendorRegistrationId) =>
        Task.FromResult(Result<NpmRegistrationRequestDto>.Failure("NPM request lookup is unavailable."));
    /// <summary>Fish / Meat vendor registration requests (identity proposals: no cash, amount or SRC). The collector sees only their own.</summary>
    Task<Result<FishMeatRegistrationRequestPage>> GetFishMeatRequestsAsync(int page = 1, int pageSize = 25) =>
        Task.FromResult(Result<FishMeatRegistrationRequestPage>.Failure("Registration requests unavailable."));
    Task<Result<FishMeatRegistrationRequestDto>> SubmitFishMeatRequestAsync(SubmitFishMeatRegistrationRequest request) =>
        Task.FromResult(Result<FishMeatRegistrationRequestDto>.Failure("Registration requests unavailable."));
    Task<Result<FishMeatRegistrationRequestDto>> CancelFishMeatRequestAsync(Guid requestId, CancelFishMeatRegistrationRequest request) =>
        Task.FromResult(Result<FishMeatRegistrationRequestDto>.Failure("Registration requests unavailable."));
    Task<Result<NpmRegistrationRequestPage>> GetNpmRequestsAsync(int page = 1, int pageSize = 25) => Task.FromResult(Result<NpmRegistrationRequestPage>.Failure("Registration requests unavailable."));
    /// <summary>Bounded, category-filtered pages of the registered sources assigned to this collector. Online only; nothing is cached or computed here.</summary>
    Task<Result<IReadOnlyList<MobileUnpaidMonthDto>>> GetUnpaidMonthsAsync(Guid stallId, Guid? contractId = null) => Task.FromResult(Result<IReadOnlyList<MobileUnpaidMonthDto>>.Failure("Unpaid months are unavailable."));
    Task<Result<ArrearsWorkspaceDto>> GetArrearsWorkspaceAsync() => Task.FromResult(Result<ArrearsWorkspaceDto>.Failure("Arrears are unavailable."));
    Task<Result<HistoricalAccessCodeDto>> IssueHistoricalAccessCodeAsync(IssueHistoricalAccessRequest request) => Task.FromResult(Result<HistoricalAccessCodeDto>.Failure("Code issuance is unavailable."));
    Task<Result<CollectionSourcePage>> BrowseCollectionSourcesAsync(string? category, string? search, int skip, int take) => Task.FromResult(Result<CollectionSourcePage>.Failure("Source browsing unavailable."));
    Task<Result<IReadOnlyList<CollectionSourceSearchResult>>> SearchCollectionSourcesAsync(string? search) => Task.FromResult(Result<IReadOnlyList<CollectionSourceSearchResult>>.Failure("Source search unavailable."));
    Task<Result<CollectionSessionDiscovery>> GetSourceCollectionDiscoveryAsync(CollectionSourceIdentity? identity) => Task.FromResult(Result<CollectionSessionDiscovery>.Failure("Source discovery unavailable."));
    Task<Result<SourceNativeChargeQuote>> QuoteOfficeCollectionAsync(SourceNativeCollectionRequest request) => Task.FromResult(Result<SourceNativeChargeQuote>.Failure("Source quote unavailable."));
    Task<Result<GovernedServiceOutcomeDto>> RecordOfficeCollectionAsync(SourceNativeCollectionRequest request) => Task.FromResult(Result<GovernedServiceOutcomeDto>.Failure("Source collection unavailable."));
    Task<Result<IReadOnlyList<SourceNativeActivityDto>>> GetOfficeActivityAsync(DateOnly from, DateOnly to, string? operationCode = null) => Task.FromResult(Result<IReadOnlyList<SourceNativeActivityDto>>.Failure("Activity unavailable."));
    Task<Result<IReadOnlyList<NpmDailyBatchSource>>> GetNpmDailyBatchSourcesAsync() => Task.FromResult(Result<IReadOnlyList<NpmDailyBatchSource>>.Failure("Batch unavailable."));
    Task<Result<NpmDailyBatchReadiness>> GetNpmDailyBatchReadinessAsync() => Task.FromResult(Result<NpmDailyBatchReadiness>.Failure("Batch unavailable."));
    Task<Result<MobileRecentCollections>> GetRecentCollectionsAsync() => Task.FromResult(Result<MobileRecentCollections>.Failure("Recent collections unavailable."));
    Task<Result<CollectionSessionQuote>> QuoteCollectionEditAsync(EditMobileCollectionIntent intent) => Task.FromResult(Result<CollectionSessionQuote>.Failure("Correction unavailable."));
    Task<Result<MobileCollectionCorrectionResult>> EditCollectionAsync(RecordMobileCollectionEditRequest request) => Task.FromResult(Result<MobileCollectionCorrectionResult>.Failure("Correction unavailable."));
    Task<Result<MobileCollectionCorrectionResult>> RemoveCollectionAsync(RemoveMobileCollectionRequest request) => Task.FromResult(Result<MobileCollectionCorrectionResult>.Failure("Correction unavailable."));
    Task<Result<NpmDailyBatchQuote>> QuoteNpmDailyBatchAsync(NpmDailyBatchIntent intent) => Task.FromResult(Result<NpmDailyBatchQuote>.Failure("Batch unavailable."));
    Task<Result<CollectionSessionResult>> RecordNpmDailyBatchAsync(RecordNpmDailyBatchRequest request) => Task.FromResult(Result<CollectionSessionResult>.Failure("Batch unavailable."));
    Task<Result<TaboBatchQuoteDto>> QuoteTaboBatchAsync(TaboBatchRequest request);
    Task<Result<TaboBatchOutcomeDto>> RecordTaboBatchAsync(TaboBatchRequest request);
    Task<Result<EcfPostOutcomeDto>> PostSpaceObligationAsync(MobileObligationPostRequest request);
    Task<Result<IReadOnlyList<DirectVendorFeeSource>>> GetDirectVendorFeeSourcesAsync() => Task.FromResult(Result<IReadOnlyList<DirectVendorFeeSource>>.Failure("Vendor sources are unavailable."));
    Task<Result<IReadOnlyList<CollectionPayorDto>>> SearchCollectionSessionPayorsAsync(string search) => Task.FromResult(Result<IReadOnlyList<CollectionPayorDto>>.Failure("Payer search is unavailable."));
    Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetMobileEcfSourcesAsync() => Task.FromResult(Result<IReadOnlyList<EcfObligationQuoteDto>>.Failure("Electricity sources are unavailable."));
    /// <summary>Read-only: the canonical Collections already posted against one utility source for a period (never a name match, never a cache).</summary>
    Task<Result<IReadOnlyList<RecentUtilityCollectionDto>>> GetRecentUtilityCollectionsAsync(Guid stallId, string utility, int year, int month) =>
        Task.FromResult(Result<IReadOnlyList<RecentUtilityCollectionDto>>.Failure("Recent collections are unavailable."));
    Task<Result<CollectionSessionDiscovery>> GetCollectionSessionDiscoveryAsync(Guid? payorId) => Task.FromResult(Result<CollectionSessionDiscovery>.Failure("Checkout is unavailable in this client."));
    Task<Result<CollectionSessionQuote>> QuoteCollectionSessionAsync(CollectionSessionIntent intent) => Task.FromResult(Result<CollectionSessionQuote>.Failure("Checkout is unavailable in this client."));
    Task<Result<CollectionSessionResult>> RecordCollectionSessionAsync(RecordCollectionSessionRequest request) => Task.FromResult(Result<CollectionSessionResult>.Failure("Checkout is unavailable in this client."));
    Task<Result<CollectionSessionResult>> GetCollectionSessionAsync(Guid sessionId) => Task.FromResult(Result<CollectionSessionResult>.Failure("Checkout is unavailable in this client."));
    Task<Result<NpmWholePaymentQuoteDto>> GetNpmWholePaymentQuoteAsync(Guid stallId, int year, int month) =>
        Task.FromResult(Result<NpmWholePaymentQuoteDto>.Failure("Whole payment is unavailable in this client."));
    Task<Result<MobileMenuDto>> GetMenuAsync();

    /// <summary>Read-only: which assigned non-facility operations are collectible now (GET api/Mobile/operations/capabilities).</summary>
    Task<Result<CollectorOperationCapabilitiesDto>> GetOperationCapabilitiesAsync();

    /// <summary>Read-only: the signed-in collector's own money and form position for a period (GET api/Mobile/position).</summary>
    Task<Result<EEMOCantilanSDS.Application.Dtos.Revenue.CollectorPositionDto>> GetMyPositionAsync(DateOnly from, DateOnly to);

    /// <summary>Read-only: the signed-in collector's own canonical collections for a period (GET api/Mobile/records/collections).</summary>
    Task<Result<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionsRegisterDto>> GetMyCollectionsAsync(DateOnly from, DateOnly to);


    /// <summary>Fish / Meat vendor-fee periods with a remaining balance, for an NPM-assigned collector (GET api/Mobile/vendor-fee-dues).</summary>
    Task<Result<IReadOnlyList<MobileVendorFeeDueDto>>> GetVendorFeeDuesAsync();

    /// <summary>The approved terms in force today for an assigned governed operation (GET api/governed-services/{code}/terms).</summary>
    Task<Result<GovernedServiceTermsDto>> GetOperationTermsAsync(string operationCode, GovernedServiceMode? mode);

    /// <summary>This collector's posted governed-operation collections for a period (GET api/governed-services/records).</summary>
    Task<Result<IReadOnlyList<GovernedServiceRecordDto>>> GetOperationRecordsAsync(DateOnly from, DateOnly to);
    Task<Result<MobileCollectorProfileDto>> GetProfileAsync();
    Task<Result<bool>> UpdateProfileAsync(UpdateMobileProfileRequest request);
    Task<Result<bool>> RegisterDeviceTokenAsync(RegisterDeviceTokenRequest request);

    /// <summary>Unregisters this device's push token (collector turned notifications off).</summary>
    Task<Result<bool>> RemoveDeviceTokenAsync(string token);
    Task<Result<IReadOnlyList<MobileCollectorRecordDto>>> GetRecordsAsync(FacilityCode? facility, DateOnly from, DateOnly to);
    Task<Result<MobileCollectorReportDto>> GetReportAsync(FacilityCode? facility, int year, int month);
    Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month);
    Task<Result<NpmMeatWeighingRateQuoteDto>> GetNpmMeatWeighingRateQuoteAsync(DateOnly businessDate);

    /// <summary>
    /// What the market is behind on: months that closed owing, and the days of this month gone by.
    /// </summary>
    /// <remarks>
    /// Its own call rather than more fields on the round, which is fetched at every stall and must stay light. Asked once, when
    /// the collector opens the arrears screen.
    /// </remarks>
    Task<Result<MobileNpmArrearsDto>> GetNpmArrearsAsync(int year, int month);
    Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request);

    /// <summary>Several owed days of one stall, settled together against one receipt, in a single transaction.</summary>
    Task<Result<bool>> SettleNpmDaysAsync(SettleMobileNpmDaysRequest request);

    /// <summary>
    /// A closed month of one stall, settled at the office's own figure for that month.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="SettleNpmDaysAsync"/> because a closed month is not a set of days: where the office lets a
    /// stall for a monthly rent, the month owes that rent whatever its calendar gave it, so settling it day by day would
    /// over-collect on a 31-day month.
    /// </remarks>
    Task<Result<bool>> SettleNpmMonthAsync(SettleMobileNpmMonthRequest request);
    Task<Result<MobileNpmUtilityDto>> GetNpmUtilityAsync(int year, int month);
    Task<Result<bool>> RecordNpmUtilityPaymentAsync(RecordMobileUtilityPaymentRequest request);
    Task<Result<IReadOnlyList<WcfObligationQuoteDto>>> GetWcfObligationsAsync(int throughYear, int throughMonth);

    /// <summary>Eligible WCF sources for a billing period, each with its prepared, settled and outstanding Water amounts.</summary>
    Task<Result<IReadOnlyList<WcfMobileSourceDto>>> GetWcfSourcesAsync(int billingYear, int billingMonth);
    Task<Result<WcfCollectionOutcomeDto>> PostWcfCollectionAsync(WcfCollectionPostRequest request);
    Task<Result<MobileMonthlyCollectionDto>> GetMonthlyCollectionAsync(FacilityCode facility, int year, int month);
    Task<Result<bool>> RecordMonthlyCollectionAsync(RecordMobileMonthlyCollectionRequest request);
    Task<Result<MobileSlaughterCollectionDto>> GetSlaughterCollectionAsync(int year, int month, int day);
    Task<Result<bool>> RecordSlaughterAsync(RecordMobileSlaughterRequest request);
    Task<Result<bool>> UpdateSlaughterAsync(UpdateMobileSlaughterRequest request);
    /// <summary>The approved Slaughterhouse charges (today's fixed rates) and whether the service is collected by approved charges.</summary>
    Task<Result<SlaughterTermsDto>> GetSlaughterTermsAsync(DateOnly date) => Task.FromResult(Result<SlaughterTermsDto>.Failure("Slaughterhouse charges are unavailable."));
    Task<Result<FeeScheduleCollectionWorkflow.SlaughterQuote>> QuoteSlaughterAsync(FeeSchedulePostRequest request) => Task.FromResult(Result<FeeScheduleCollectionWorkflow.SlaughterQuote>.Failure("Slaughterhouse quote is unavailable."));
    Task<Result<MobileTrmCollectionDto>> GetTrmCollectionAsync();
    Task<Result<TrmTripDto>> RecordTripAsync(RecordMobileTripRequest request);
    Task<Result<TrmTransporterDto>> AddTransporterAsync(string name, string organization, string route, string plate);
    Task<Result<MobileTpmCollectionDto>> GetTpmCollectionAsync();
    Task<Result<TpmVendorAttendanceDto>> AddTpmVendorAsync(AddMobileTpmVendorRequest request);
    Task<Result<bool>> MarkTpmVendorPaidAsync(MarkMobileTpmVendorPaidRequest request);
    Task<Result<bool>> HideSuggestionAsync(HideMobileSuggestionRequest request);

    /// <summary>Replays a batch of queued offline collections; returns a per-item sync outcome.</summary>
    Task<Result<SyncOfflineCollectionsResultDto>> SyncOfflineCollectionsAsync(
        EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections.SyncOfflineCollectionsCommand command);

    /// <summary>Issues a single-use payor activation code for a stall (collector-facility guarded server-side).</summary>
    Task<Result<StallActivationCodeDto>> GenerateActivationCodeAsync(GenerateStallActivationCodeCommand command);

    /// <summary>Encodes the manual OR for an online payment awaiting OR (preserves online attribution; completes the transaction).</summary>
    Task<Result<bool>> IssueOnlinePaymentOrNumberAsync(Guid transactionId, string orNumber);

    /// <summary>Resolves a collector-app bind token to its LGU + branding (anonymous, pre-login).</summary>
    Task<Result<MobileBindInfoDto>> GetBindInfoAsync(string token);

    /// <summary>Gets the latest published app version for the in-app update check (anonymous).</summary>
    Task<Result<MobileAppVersionDto>> GetAppVersionAsync();
}
