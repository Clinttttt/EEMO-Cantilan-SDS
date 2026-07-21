using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.RemoveNpmCustomSection;

/// <summary>
/// Removes a custom NPM section from the current tenant's registry (Head-only). Guarded: fails if any
/// stall still uses the section.
/// </summary>
public record RemoveNpmCustomSectionCommand(string Name) : IRequest<Result<bool>>;
