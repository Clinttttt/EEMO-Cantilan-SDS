using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;
public sealed class OfficeSourcesApiClient(HttpClient http) : HandleResponse(http), IOfficeSourcesApiClient
{
    public Task<Result<IReadOnlyList<FishMeatVendorRegistrationDto>>> RegistrationsAsync(int year) => GetAsync<IReadOnlyList<FishMeatVendorRegistrationDto>>($"api/office-sources/fish-meat/registrations?year={year}");
    public Task<Result<FishMeatVendorRegistrationDto>> RegisterAsync(RegisterFishMeatVendorRequest request) => PostAsync<RegisterFishMeatVendorRequest, FishMeatVendorRegistrationDto>("api/office-sources/fish-meat/registrations", request);
    public Task<Result<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoicesAsync(DateOnly date) => GetAsync<IReadOnlyList<TerminalVehicleChoice>>($"api/office-sources/terminal/vehicle-choices?date={date:yyyy-MM-dd}");
    public Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request) => PutAsync<TerminalVehicleMappingRequest, bool>("api/office-sources/terminal/vehicle-section", request);
    public Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to, string? operationCode = null) =>
        GetAsync<IReadOnlyList<SourceNativeActivityDto>>($"api/office-sources/activity?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&operationCode={Uri.EscapeDataString(operationCode ?? "")}");
}
