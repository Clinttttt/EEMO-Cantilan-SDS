using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;
public sealed class OfficeSourcesApiClient(HttpClient http) : HandleResponse(http), IOfficeSourcesApiClient
{
    public Task<Result<NpmRegistrationRequestPage>> NpmRequestsAsync(int page = 1, int pageSize = 25) => GetAsync<NpmRegistrationRequestPage>($"api/office-sources/npm-requests?page={page}&pageSize={pageSize}");
    public Task<Result<NpmRegistrationRequestDto>> ReviewNpmRequestAsync(Guid id, ReviewNpmRegistrationRequest request) => PostAsync<ReviewNpmRegistrationRequest, NpmRegistrationRequestDto>($"api/office-sources/npm-requests/{id}/review", request);
    public Task<Result<NpmVendorOccupancyResult>> CreateNpmVendorOccupancyAsync(CreateNpmVendorOccupancyRequest request) =>
        PostAsync<CreateNpmVendorOccupancyRequest, NpmVendorOccupancyResult>("api/office-sources/fish-meat/npm-occupancies", request);
    public Task<Result<NpmVendorCandidates>> NpmVendorCandidatesAsync(int taxYear, bool historical = false, bool exceptions = false) =>
        GetAsync<NpmVendorCandidates>($"api/office-sources/fish-meat/npm-candidates?taxYear={taxYear}&historical={historical}&exceptions={exceptions}");
    public Task<Result<IReadOnlyList<NpmVendorAssociationDto>>> NpmVendorLinksAsync(Guid vendorId) =>
        GetAsync<IReadOnlyList<NpmVendorAssociationDto>>($"api/office-sources/fish-meat/registrations/{vendorId}/npm-links");
    public Task<Result<NpmVendorAssociationDto>> LinkNpmVendorAsync(Guid vendorId, ConfirmNpmVendorAssociationRequest request) =>
        PostAsync<ConfirmNpmVendorAssociationRequest, NpmVendorAssociationDto>($"api/office-sources/fish-meat/registrations/{vendorId}/npm-links", request);
    public Task<Result<NpmVendorAssociationDto>> CorrectNpmVendorLinkAsync(Guid id, CorrectNpmVendorAssociationRequest request) =>
        PostAsync<CorrectNpmVendorAssociationRequest, NpmVendorAssociationDto>($"api/office-sources/fish-meat/npm-links/{id}/correct", request);
    public Task<Result<IReadOnlyList<FishMeatVendorRegistrationDto>>> RegistrationsAsync(int year) => GetAsync<IReadOnlyList<FishMeatVendorRegistrationDto>>($"api/office-sources/fish-meat/registrations?year={year}");
    public Task<Result<FishMeatVendorRegistrationDto>> RegisterAsync(RegisterFishMeatVendorRequest request) => PostAsync<RegisterFishMeatVendorRequest, FishMeatVendorRegistrationDto>("api/office-sources/fish-meat/registrations", request);
    public Task<Result<VendorRegistryImportPreview>> PreviewImportAsync(VendorRegistryImportRequest request) => PostAsync<VendorRegistryImportRequest, VendorRegistryImportPreview>("api/office-sources/fish-meat/import/preview", request);
    public Task<Result<VendorRegistryImportResult>> SaveImportAsync(VendorRegistryImportRequest request) => PostAsync<VendorRegistryImportRequest, VendorRegistryImportResult>("api/office-sources/fish-meat/import/save", request);
    public Task<Result<VendorRegistrySummary>> RegistrySummaryAsync(int year) => GetAsync<VendorRegistrySummary>($"api/office-sources/fish-meat/summary?year={year}");
    public Task<Result<VendorRegistryManagement>> ManageRegistrationsAsync(int taxYear, int month, EEMOCantilanSDS.Domain.Entities.Revenue.VendorRegistrationStatus? status = null, int? year = null) =>
        GetAsync<VendorRegistryManagement>($"api/office-sources/fish-meat/manage?taxYear={taxYear}&month={month}&status={status}&year={year}");
    public Task<Result<VendorRegistrationMutationResult>> CloseRegistrationAsync(Guid id, CloseVendorRegistrationRequest request) =>
        PostAsync<CloseVendorRegistrationRequest, VendorRegistrationMutationResult>($"api/office-sources/fish-meat/registrations/{id}/close", request);
    public Task<Result<VendorRegistrationMutationResult>> RenewRegistrationAsync(Guid id, RenewVendorRegistrationRequest request) =>
        PostAsync<RenewVendorRegistrationRequest, VendorRegistrationMutationResult>($"api/office-sources/fish-meat/registrations/{id}/renew", request);
    public Task<Result<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoicesAsync(DateOnly date) => GetAsync<IReadOnlyList<TerminalVehicleChoice>>($"api/office-sources/terminal/vehicle-choices?date={date:yyyy-MM-dd}");
    public Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request) => PutAsync<TerminalVehicleMappingRequest, bool>("api/office-sources/terminal/vehicle-section", request);
    public Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to, string? operationCode = null) =>
        GetAsync<IReadOnlyList<SourceNativeActivityDto>>($"api/office-sources/activity?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&operationCode={Uri.EscapeDataString(operationCode ?? "")}");
}
