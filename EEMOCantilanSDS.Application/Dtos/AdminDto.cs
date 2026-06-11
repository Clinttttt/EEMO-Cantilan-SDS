using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Dtos;

public record AdminDto(
    Guid Id,
    string FullName,
    string Username,
    string Email,
    AdminRole Role,
    bool IsActive);
