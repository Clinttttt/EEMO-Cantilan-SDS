using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The office's ECF / WCF source register and optional amount preparation (server-filtered, server-paged).</summary>
public interface IUtilityRegisterApiClient
{
    Task<Result<UtilityRegisterPageDto>> GetRegisterAsync(UtilityRegisterRequest request);
    Task<Result<UtilityPrepareResult>> PrepareAsync(UtilityPrepareRequest request);
}
