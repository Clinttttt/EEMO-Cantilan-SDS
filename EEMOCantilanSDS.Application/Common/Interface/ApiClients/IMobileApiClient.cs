using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMobileApiClient
{
    Task<Result<MobileMenuDto>> GetMenuAsync();
    Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month);
    Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request);
}
