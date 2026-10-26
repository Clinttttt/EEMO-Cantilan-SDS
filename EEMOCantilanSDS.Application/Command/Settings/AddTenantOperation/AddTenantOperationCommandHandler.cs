using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Settings.AddTenantOperation;

/// <remarks>
/// Idempotent and race-safe: a second submission finds the operation already listed and changes nothing. An existing revenue source of any state is
/// reused as it is - never duplicated, and a retired one is never silently reactivated (it is reported, and the operation stays Setup required).
/// The listing and the source are saved together or not at all.
/// </remarks>
public sealed class AddTenantOperationCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<AddTenantOperationCommand, Result<AddTenantOperationResultDto>>
{
    private const string Conflict = "This operation was changed by someone else. Reload the page and try again.";

    public async Task<Result<AddTenantOperationResultDto>> Handle(AddTenantOperationCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated
            || !string.Equals(currentUser.Role, "SuperAdmin", StringComparison.Ordinal)
            || currentUser.MunicipalityId is not { } tenantId || tenantId == Guid.Empty)
            return Result<AddTenantOperationResultDto>.Forbidden();

        var entry = TenantOperationCatalog.Find(request.OperationCode);
        if (entry is null)
            return Result<AddTenantOperationResultDto>.Failure("That is not a revenue operation StallTrack recognises.", ResultStatus.Invalid);

        var by = currentUser.Username ?? "Head";
        var rows = await context.TenantOperationSettings.Where(s => s.MunicipalityId == tenantId).ToListAsync(ct);

        // A municipality that never adopted configuration already lists every operation under its established name: there is nothing to add.
        if (rows.Count == 0)
            return Result<AddTenantOperationResultDto>.Success(await ResultAsync(tenantId, alreadyAdded: true, false, false, ct));

        var row = rows.First(r => r.OperationCode == entry.Code);
        if (row.IsListed)
            return Result<AddTenantOperationResultDto>.Success(await ResultAsync(tenantId, alreadyAdded: true, false, false, ct));
        if (request.ExpectedVersion is { } expected && row.Version != expected)
            return Result<AddTenantOperationResultDto>.Failure(Conflict, ResultStatus.Conflict);

        var problem = TenantOperationCatalog.LabelProblem(entry.Code, request.LocalDisplayName, rows.Select(r => (r.OperationCode, r.LocalDisplayName, r.IsListed)));
        if (problem is not null)
            return Result<AddTenantOperationResultDto>.Failure(problem, ResultStatus.Invalid);

        row.Update(true, request.LocalDisplayName, by);

        var source = await context.RevenueClassifications
            .FirstOrDefaultAsync(x => x.MunicipalityId == tenantId && x.SemanticCode == entry.ClassificationCode, ct);
        var created = source is null;
        if (created) context.RevenueClassifications.Add(RevenueClassification.Create(entry.ClassificationCode, tenantId, by));

        try { await context.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or DbUpdateException)
        {
            context.ChangeTracker.Clear();
            // A competing add of the same operation (or its source) won: if it is listed now, the outcome is the one asked for.
            var winner = await context.TenantOperationSettings.AsNoTracking()
                .AnyAsync(s => s.MunicipalityId == tenantId && s.OperationCode == entry.Code && s.IsListed, ct);
            return winner
                ? Result<AddTenantOperationResultDto>.Success(await ResultAsync(tenantId, alreadyAdded: true, false, false, ct))
                : Result<AddTenantOperationResultDto>.Failure(Conflict, ResultStatus.Conflict);
        }

        return Result<AddTenantOperationResultDto>.Success(await ResultAsync(tenantId, false, created, source is { IsActive: false }, ct));
    }

    private async Task<AddTenantOperationResultDto> ResultAsync(Guid tenantId, bool alreadyAdded, bool created, bool retired, CancellationToken ct) =>
        new(await TenantOperationSettingsStore.ReadAsync(context, tenantId, ct), alreadyAdded, created, retired);
}
