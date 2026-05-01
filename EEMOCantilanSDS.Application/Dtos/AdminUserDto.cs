using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
public class AdminUserDto {

    public string UserId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public AdminRole AdminRole { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }

}