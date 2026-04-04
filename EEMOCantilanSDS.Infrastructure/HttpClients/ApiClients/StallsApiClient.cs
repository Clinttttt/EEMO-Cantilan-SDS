using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients
{
    public class StallsApiClient : HandleResponse, IStallsApiClient
    {
        public StallsApiClient(HttpClient http) : base(http)
        {
        }

        public async Task<Result<StallHoldersListDto>> GetStallHoldersList() => await GetAsync<StallHoldersListDto>("api/Stalls/facility/{facilityCode}/holders-list");  
    }
}
