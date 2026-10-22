using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;
public interface IOfficeSourcesApiClient
{
    Task<Result<NpmVendorOccupancyResult>> CreateNpmVendorOccupancyAsync(CreateNpmVendorOccupancyRequest request);
    Task<Result<NpmRegistrationRequestPage>> NpmRequestsAsync(int page = 1, int pageSize = 25);
    /// <summary>The Fish / Meat registration-request queue (identity proposals, no money). Head and Admin read; only the Head reviews or completes.</summary>
    Task<Result<FishMeatRegistrationRequestPage>> FishMeatRequestsAsync(int page = 1, int pageSize = 25, FishMeatRequestStatus? status = null) =>
        Task.FromResult(Result<FishMeatRegistrationRequestPage>.Failure("Registration requests unavailable."));
    Task<Result<FishMeatRegistrationRequestDto>> ReviewFishMeatRequestAsync(Guid id, ReviewFishMeatRegistrationRequest request) =>
        Task.FromResult(Result<FishMeatRegistrationRequestDto>.Failure("Registration requests unavailable."));
    Task<Result<FishMeatRegistrationRequestDto>> CompleteFishMeatRequestAsync(Guid id, CompleteFishMeatRegistrationRequest request) =>
        Task.FromResult(Result<FishMeatRegistrationRequestDto>.Failure("Registration requests unavailable."));
    Task<Result<NpmRegistrationRequestDto>> ReviewNpmRequestAsync(Guid id, ReviewNpmRegistrationRequest request);
    Task<Result<NpmVendorCandidates>> NpmVendorCandidatesAsync(int taxYear, bool historical = false, bool exceptions = false);
    Task<Result<IReadOnlyList<NpmVendorAssociationDto>>> NpmVendorLinksAsync(Guid vendorId);
    Task<Result<NpmVendorAssociationDto>> LinkNpmVendorAsync(Guid vendorId, ConfirmNpmVendorAssociationRequest request);
    Task<Result<NpmVendorAssociationDto>> CorrectNpmVendorLinkAsync(Guid id, CorrectNpmVendorAssociationRequest request);
    Task<Result<IReadOnlyList<FishMeatVendorRegistrationDto>>> RegistrationsAsync(int year);
    Task<Result<FishMeatVendorRegistrationDto>> RegisterAsync(RegisterFishMeatVendorRequest request);
    Task<Result<VendorRegistryImportPreview>> PreviewImportAsync(VendorRegistryImportRequest request);
    Task<Result<VendorRegistryImportResult>> SaveImportAsync(VendorRegistryImportRequest request);
    Task<Result<VendorRegistrySummary>> RegistrySummaryAsync(int year);
    Task<Result<VendorRegistryManagement>> ManageRegistrationsAsync(int taxYear, int month, EEMOCantilanSDS.Domain.Entities.Revenue.VendorRegistrationStatus? status = null, int? year = null);
    Task<Result<VendorRegistrationMutationResult>> CorrectOfficeDetailsAsync(Guid id, CorrectVendorOfficeDetailsRequest request);
    Task<Result<VendorRegistrationMutationResult>> CloseRegistrationAsync(Guid id, CloseVendorRegistrationRequest request);
    Task<Result<VendorRegistrationMutationResult>> DeleteRegistrationAsync(Guid id, DeleteVendorRegistrationRequest request) =>
        Task.FromResult(Result<VendorRegistrationMutationResult>.Failure("Registration deletion is unavailable.", ResultStatus.Failed));
    Task<Result<VendorRegistrationMutationResult>> RenewRegistrationAsync(Guid id, RenewVendorRegistrationRequest request);
    Task<Result<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoicesAsync(DateOnly date);
    Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request);
    Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to, string? operationCode = null);
}
