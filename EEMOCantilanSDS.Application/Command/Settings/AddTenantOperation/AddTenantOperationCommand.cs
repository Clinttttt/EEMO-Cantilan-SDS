using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Settings.AddTenantOperation;

/// <summary>
/// A Head adds a revenue operation the municipality did not select at onboarding: it is listed in the directory (with an optional name of its own) and its
/// revenue source is created if none exists. It does NOT authorize collection: no policy, instrument, collector assignment, activation or rate is created, so
/// the operation reads Setup required until the office's own setup exists.
/// </summary>
public sealed record AddTenantOperationCommand(string OperationCode, string? LocalDisplayName, long? ExpectedVersion = null)
    : IRequest<Result<AddTenantOperationResultDto>>;
