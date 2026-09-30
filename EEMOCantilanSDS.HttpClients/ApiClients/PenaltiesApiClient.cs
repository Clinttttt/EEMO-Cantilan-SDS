using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class PenaltiesApiClient(HttpClient http) : HandleResponse(http), IPenaltiesApiClient
{
    public Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync() =>
        GetAsync<IReadOnlyList<PenaltyDefinitionDto>>("api/penalties/definitions");

    public Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request) =>
        PostAsync<DefinePenaltyRequest, PenaltyDefinitionDto>("api/penalties/definitions", request);

    public Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<PenaltyRegisterRowDto>>(
            $"api/penalties/register?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
