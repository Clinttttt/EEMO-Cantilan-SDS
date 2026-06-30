using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;

/// <summary>
/// Returns the read-only system configuration overview. <paramref name="Environment"/> is supplied
/// by the API from the host (e.g. Production/Development); everything else is sourced from the
/// domain constants so the page never drifts from the live system rules.
/// </summary>
public record GetSystemSettingsQuery(string Environment) : IRequest<Result<SystemSettingsDto>>;
