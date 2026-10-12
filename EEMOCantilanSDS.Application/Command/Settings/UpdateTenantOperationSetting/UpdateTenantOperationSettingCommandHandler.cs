using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;

public sealed class UpdateTenantOperationSettingCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<UpdateTenantOperationSettingCommand, Result<TenantOperationSettingsDto>>
{
    private const string Conflict = "This setting was changed by someone else. Reload the page and try again.";

    public async Task<Result<TenantOperationSettingsDto>> Handle(UpdateTenantOperationSettingCommand request, CancellationToken ct)
    {
        // Defence in depth beside the controller's [Authorize]: only the municipality's Head may change its configuration, and
        // only its own. The municipality comes from the token, never the request.
        if (!currentUser.IsAuthenticated
            || !string.Equals(currentUser.Role, "SuperAdmin", StringComparison.Ordinal)
            || currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<TenantOperationSettingsDto>.Forbidden();

        if (!TenantOperationCatalog.IsKnown(request.OperationCode))
            return Result<TenantOperationSettingsDto>.Failure("That is not a revenue operation StallTrack recognises.", ResultStatus.Invalid);

        var by = currentUser.Username ?? "Head";
        var rows = await context.TenantOperationSettings.Where(s => s.MunicipalityId == municipalityId).ToListAsync(ct);

        // First edit by a municipality that never adopted configuration: record today's state as explicit rows (everything
        // listed, under its established names) so editing ONE operation cannot make the rest vanish from the directory.
        if (rows.Count == 0) rows = TenantOperationSettingsStore.AdoptLegacy(context, municipalityId, by).ToList();

        var row = rows.First(r => r.OperationCode == request.OperationCode);
        if (request.ExpectedVersion is { } expected && row.Version != expected)
            return Result<TenantOperationSettingsDto>.Failure(Conflict, ResultStatus.Conflict);

        var problem = TenantOperationCatalog.LabelProblem(
            request.OperationCode, request.LocalDisplayName,
            rows.Select(r => (r.OperationCode, r.LocalDisplayName, r.IsListed)));
        if (problem is not null)
            return Result<TenantOperationSettingsDto>.Failure(problem, ResultStatus.Invalid);

        row.Update(request.IsListed, request.LocalDisplayName, by);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<TenantOperationSettingsDto>.Failure(Conflict, ResultStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            // Two first edits racing to adopt both insert the same unique (municipality, operation) rows; the loser retries.
            return Result<TenantOperationSettingsDto>.Failure(
                "The operation settings were being set up at the same moment. Try again.", ResultStatus.Conflict);
        }

        return Result<TenantOperationSettingsDto>.Success(await TenantOperationSettingsStore.ReadAsync(context, municipalityId, ct));
    }
}
