using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMobileApiClient
{
    Task<Result<MobileMenuDto>> GetMenuAsync();
}
