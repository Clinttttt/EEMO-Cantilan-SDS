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
    DateTime CreatedAt,
    // False until the address is confirmed through its emailed link. An unconfirmed address cannot receive
    // password-reset links, so the roster surfaces this to make a typo (or a missed email) visible.
    bool EmailVerified = false);
