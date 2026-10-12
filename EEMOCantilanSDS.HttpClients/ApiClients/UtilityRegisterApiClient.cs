using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class UtilityRegisterApiClient(HttpClient http) : HandleResponse(http), IUtilityRegisterApiClient
{
    public Task<Result<UtilityRegisterPageDto>> GetRegisterAsync(UtilityRegisterRequest r)
    {
        var query = new List<string>
        {
            $"kind={(int)r.Kind}", $"year={r.Year}", $"month={r.Month}",
            $"skip={r.Skip.ToString(CultureInfo.InvariantCulture)}", $"take={r.Take.ToString(CultureInfo.InvariantCulture)}"
        };
        if (r.Facility is { } facility) query.Add($"facility={(int)facility}");
        if (!string.IsNullOrWhiteSpace(r.Section)) query.Add($"section={Uri.EscapeDataString(r.Section)}");
        if (!string.IsNullOrWhiteSpace(r.Status)) query.Add($"status={Uri.EscapeDataString(r.Status)}");
        if (!string.IsNullOrWhiteSpace(r.Search)) query.Add($"search={Uri.EscapeDataString(r.Search.Trim())}");
        return GetAsync<UtilityRegisterPageDto>("api/utility-register?" + string.Join("&", query));
    }

    public Task<Result<UtilityPrepareResult>> PrepareAsync(UtilityPrepareRequest request) =>
        PostAsync<UtilityPrepareRequest, UtilityPrepareResult>("api/utility-register/prepare", request);
}
