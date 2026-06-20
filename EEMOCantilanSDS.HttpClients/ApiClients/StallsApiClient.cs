using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class StallsApiClient(HttpClient http) : HandleResponse(http), IStallsApiClient
{
    public async Task<Result<StallHoldersListDto>> GetStallHoldersListAsync(
        FacilityCode facilityCode, 
        MarketSection? section = null, 
        string? searchTerm = null)
    {
        var query = $"api/Stalls/facility/{facilityCode}/holders-list";
        var queryParams = new List<string>();
        
        if (section.HasValue)
            queryParams.Add($"section={section.Value}");
        
        if (!string.IsNullOrWhiteSpace(searchTerm))
            queryParams.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        
        if (queryParams.Any())
            query += "?" + string.Join("&", queryParams);
        
        return await GetAsync<StallHoldersListDto>(query);
    }

    public async Task<Result<CursorPagedResult<StallDto>>> GetStallsByFacilityPaginatedAsync(
        FacilityCode facilityCode,
        MarketSection? section = null,
        DateTime? cursor = null,
        int pageSize = 20)
    {
        var query = $"api/Stalls/facility/{facilityCode}/paginated?pageSize={pageSize}";
        
        if (section.HasValue)
            query += $"&section={section.Value}";
        
        if (cursor.HasValue)
            query += $"&cursor={cursor.Value:O}";
        
        return await GetAsync<CursorPagedResult<StallDto>>(query);
    }

    public async Task<Result<StallDto>> CreateStallAsync(CreateStallCommand command) =>
        await PostAsync<CreateStallCommand, StallDto>("api/Stalls", command);

    public async Task<Result<StallDto>> UpdateStallAsync(Guid stallId, UpdateStallCommand command) =>
        await PutAsync<UpdateStallCommand, StallDto>($"api/Stalls/{stallId}", command);

    public async Task<Result<bool>> ToggleStallStatusAsync(Guid stallId, bool close) =>
        await UpdateAsync<ToggleStallStatusRequest, bool>($"api/Stalls/{stallId}/status", new ToggleStallStatusRequest(close));

    public async Task<Result<bool>> UpdateStallDetailsAsync(Guid stallId, UpdateStallDetailsCommand command) =>
        await UpdateAsync<UpdateStallDetailsCommand, bool>($"api/Stalls/{stallId}/details", command);
}
