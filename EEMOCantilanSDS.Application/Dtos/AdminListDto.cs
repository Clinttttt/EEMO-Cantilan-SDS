using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Dtos;

public record AdminListDto(
    Guid Id,
    string FullName,
    string Username,
    string Email,
    AdminRole Role,
    bool IsActive,
    bool MustChangePassword,
    DateTime? LastLoginAt,
    DateTime CreatedAt);
