using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view and Head setup of the vehicle classes and approved rates for Transportation / Parking.</summary>
public interface IVehicleClassesApiClient
{
    Task<Result<IReadOnlyList<VehicleClassDto>>> GetAsync();
    Task<Result<VehicleClassDto>> SaveAsync(SaveVehicleClassRequest request);
    Task<Result<VehicleClassDto>> SetActiveAsync(Guid id, bool isActive);
}
