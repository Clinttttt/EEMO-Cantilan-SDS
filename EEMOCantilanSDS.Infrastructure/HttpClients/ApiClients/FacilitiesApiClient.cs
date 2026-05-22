using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class FacilitiesApiClient(HttpClient http) : HandleResponse(http), IFacilitiesApiClient
{
    public async Task<Result<FacilityReportsDto>> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month = null,
        int? weekNumber = null)
    {
        var query = $"api/Facilities/{facilityCode}/reports?period={period}&year={year}";
        
        if (month.HasValue)
            query += $"&month={month.Value}";
        
        if (weekNumber.HasValue)
            query += $"&weekNumber={weekNumber.Value}";
        
        return await GetAsync<FacilityReportsDto>(query);
    }
}
