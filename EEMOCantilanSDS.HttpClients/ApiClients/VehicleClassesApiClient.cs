using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class VehicleClassesApiClient(HttpClient http) : HandleResponse(http), IVehicleClassesApiClient
{
    public Task<Result<IReadOnlyList<VehicleClassDto>>> GetAsync() =>
        GetAsync<IReadOnlyList<VehicleClassDto>>("api/vehicle-classes");

    public Task<Result<VehicleClassDto>> SaveAsync(SaveVehicleClassRequest request) =>
        PostAsync<SaveVehicleClassRequest, VehicleClassDto>("api/vehicle-classes", request);

    public Task<Result<VehicleClassDto>> SetActiveAsync(Guid id, bool isActive) =>
        PostAsync<bool, VehicleClassDto>($"api/vehicle-classes/{id}/active", isActive);
}
