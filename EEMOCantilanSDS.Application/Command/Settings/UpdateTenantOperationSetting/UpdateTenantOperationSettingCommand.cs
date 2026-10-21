using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;

/// <summary>
/// A Head's edit of one operation's listing and local name. Presentation only: no classification, instrument, source,
/// Collector assignment or rate is read or changed here.
/// </summary>
/// <param name="ExpectedVersion">The <c>Version</c> the editor was looking at; a different stored version is refused as a conflict. Null skips the check.</param>
public sealed record UpdateTenantOperationSettingCommand(
    string OperationCode, bool IsListed, string? LocalDisplayName, long? ExpectedVersion = null)
    : IRequest<Result<TenantOperationSettingsDto>>;

public sealed record UpdateTenantOperationSettingRequest(bool IsListed, string? LocalDisplayName, long? ExpectedVersion = null);
