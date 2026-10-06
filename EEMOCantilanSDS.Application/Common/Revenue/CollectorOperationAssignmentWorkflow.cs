using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Head-managed non-facility operation permissions. An assignment is not evidence that its source,
/// policy, accountable-document stock, settlement authority, or Mobile writer is ready.
/// </summary>
public sealed class CollectorOperationAssignmentWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality)
{
    internal static readonly (string Code, string Name)[] Catalog =
    [
        (CollectorOperationCodes.Wcf, "Water Consumption Fee"),
        (CollectorOperationCodes.KanmanggaySpaceRental, "Kanmanggay Space Rental"),
        (CollectorOperationCodes.FiestaArawLotRental, "Fiesta / Araw Lot Rental"),
        (CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental"),
        (CollectorOperationCodes.LandingBerthing, "Landing / Berthing"),
        (CollectorOperationCodes.TransferLargeCattle, "Transfer Large Cattle"),
        (CollectorOperationCodes.MarketFees, "Market Fees"),
        (CollectorOperationCodes.Transportation, "Transportation / Parking")
    ];

    public async Task<Result<IReadOnlyList<CollectorOperationAssignmentDto>>> ListAsync(
        Guid collectorId, CancellationToken ct = default)
    {
        if (!TryGetTenant(out var tenantId))
            return Result<IReadOnlyList<CollectorOperationAssignmentDto>>.Forbidden();
        if (!await db.CollectorUsers.AsNoTracking().AnyAsync(x =>
                x.MunicipalityId == tenantId && x.Id == collectorId, ct))
            return Result<IReadOnlyList<CollectorOperationAssignmentDto>>.NotFound();

        var assigned = await db.CollectorOperationAssignments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.CollectorId == collectorId)
            .Select(x => x.OperationCode)
            .ToListAsync(ct);
        var assignedCodes = assigned.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<CollectorOperationAssignmentDto> result = Catalog
            .Select(x => new CollectorOperationAssignmentDto(x.Code, x.Name, assignedCodes.Contains(x.Code)))
            .ToArray();
        return Result<IReadOnlyList<CollectorOperationAssignmentDto>>.Success(result);
    }

    public async Task<Result<bool>> ReplaceAsync(
        Guid collectorId,
        ReplaceCollectorOperationAssignmentsRequest? request,
        CancellationToken ct = default)
    {
        if (!TryGetTenant(out var tenantId))
            return Result<bool>.Forbidden();
        if (!await db.CollectorUsers.AnyAsync(x =>
                x.MunicipalityId == tenantId && x.Id == collectorId, ct))
            return Result<bool>.NotFound();
        if (request?.OperationCodes is not { } requested
            || requested.Count > Catalog.Length
            || requested.Any(string.IsNullOrWhiteSpace))
            return Result<bool>.Failure("Choose valid collector operations.", ResultStatus.Invalid);

        var codes = requested.ToArray();
        if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
            return Result<bool>.Failure("An operation was selected more than once.", ResultStatus.Invalid);
        var supported = Catalog.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        if (codes.Any(x => !supported.Contains(x)))
            return Result<bool>.Failure("One or more operation codes are not supported.", ResultStatus.Invalid);

        var existing = await db.CollectorOperationAssignments
            .Where(x => x.MunicipalityId == tenantId && x.CollectorId == collectorId)
            .ToListAsync(ct);
        var desired = codes.ToHashSet(StringComparer.Ordinal);
        db.CollectorOperationAssignments.RemoveRange(existing.Where(x => !desired.Contains(x.OperationCode)));
        var current = existing.Select(x => x.OperationCode).ToHashSet(StringComparer.Ordinal);
        var actor = currentUser.Username?.Trim();
        if (string.IsNullOrEmpty(actor))
            actor = currentUser.UserId!.Value.ToString("D");

        foreach (var code in codes.Where(x => !current.Contains(x)))
            db.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                tenantId, collectorId, code, actor));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<bool>.Failure("Collector operation assignments changed concurrently. Reload and try again.",
                ResultStatus.Conflict);
        }

        return Result<bool>.Success(true);
    }

    private bool TryGetTenant(out Guid tenantId)
    {
        tenantId = municipality.MunicipalityId;
        return currentUser.IsAuthenticated
            && currentUser.Role == "SuperAdmin"
            && currentUser.UserId is { } actorId && actorId != Guid.Empty
            && tenantId != Guid.Empty
            && (currentUser.MunicipalityId is not { } tokenTenant || tokenTenant == tenantId);
    }
}
