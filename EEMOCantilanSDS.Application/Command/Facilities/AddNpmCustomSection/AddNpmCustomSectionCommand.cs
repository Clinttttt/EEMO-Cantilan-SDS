using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;

/// <summary>Adds a custom NPM section name to the current tenant's registry (Head-only). Idempotent.</summary>
public record AddNpmCustomSectionCommand(string Name) : IRequest<Result<bool>>;
