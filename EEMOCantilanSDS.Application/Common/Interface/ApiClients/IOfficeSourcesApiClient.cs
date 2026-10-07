using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;
public interface IOfficeSourcesApiClient
{
    Task<Result<IReadOnlyList<FishMeatVendorRegistrationDto>>> RegistrationsAsync(int year);
    Task<Result<FishMeatVendorRegistrationDto>> RegisterAsync(RegisterFishMeatVendorRequest request);
    Task<Result<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoicesAsync(DateOnly date);
    Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request);
    Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to, string? operationCode = null);
}
