using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    AdminUserDto? GetCurrentUser();

}
