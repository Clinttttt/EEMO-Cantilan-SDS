using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;
public interface IOfficeSourcesApiClient
{
    Task<Result<NpmVendorOccupancyResult>> CreateNpmVendorOccupancyAsync(CreateNpmVendorOccupancyRequest request);
    Task<Result<NpmRegistrationRequestPage>> NpmRequestsAsync(int page = 1, int pageSize = 25);
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
    Task<Result<VendorRegistrationMutationResult>> RenewRegistrationAsync(Guid id, RenewVendorRegistrationRequest request);
    Task<Result<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoicesAsync(DateOnly date);
    Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request);
    Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to, string? operationCode = null);
}
