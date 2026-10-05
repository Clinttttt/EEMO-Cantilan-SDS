using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Vehicle classes and their approved effective-dated rates for Transportation / Parking (IA-030, IA-050). The Head defines
/// classes and rates; Head and Admin read them. A rate change is prospective and appends a version, so a posted collection
/// keeps the rate it was charged and no historical trip is ever re-priced or given a class.
/// </summary>
public sealed class VehicleClassWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public Task<Result<IReadOnlyList<VehicleClassDto>>> GetAsync(CancellationToken ct = default) =>
        Run<IReadOnlyList<VehicleClassDto>>(async (tenantId, _, _) =>
        {
            var classes = await db.VehicleClasses.AsNoTracking().Where(x => x.MunicipalityId == tenantId).ToListAsync(ct);
            var ids = classes.Select(x => x.Id).ToArray();
            var rates = (await db.VehicleClassRates.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.VehicleClassId)).ToListAsync(ct))
                .ToLookup(x => x.VehicleClassId);
            var today = BusinessToday;
            return Result<IReadOnlyList<VehicleClassDto>>.Success(classes
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(c =>
                {
                    var current = VehicleClassRate.Resolve(rates[c.Id], today);
                    return new VehicleClassDto(c.Id, c.Code, c.DisplayName, c.IsActive, current?.Amount, current?.EffectiveDate,
                        rates[c.Id].OrderByDescending(r => r.EffectiveDate).ThenBy(r => r.Id)
                            .Select(r => new VehicleClassRateVersionDto(r.Id, r.EffectiveDate, r.Amount, r.CreatedBy, r.CreatedAtUtc)).ToList());
                }).ToList());
        }, ct);

    /// <summary>Creates a class or renames an existing one, and appends its approved rate from an effective date.</summary>
    public Task<Result<VehicleClassDto>> SaveAsync(SaveVehicleClassRequest request, CancellationToken ct = default) =>
        Run<VehicleClassDto>(async (tenantId, actor, role) =>
        {
            if (role != "SuperAdmin") return Result<VehicleClassDto>.Forbidden();
            if (request.EffectiveDate < new DateOnly(2020, 1, 1) || request.EffectiveDate > BusinessToday.AddDays(366))
                return Result<VehicleClassDto>.Failure("Choose a realistic effective date (not more than a year ahead).", ResultStatus.Invalid);
            var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
            try
            {
                var existing = await db.VehicleClasses.SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Code == code, ct);
                if (existing is null)
                {
                    existing = VehicleClass.Create(tenantId, code, request.DisplayName, actor);
                    db.VehicleClasses.Add(existing);
                }
                else
                {
                    existing.Rename(request.DisplayName);
                    existing.SetActive(true);
                    var latest = await db.VehicleClassRates.AsNoTracking()
                        .Where(x => x.MunicipalityId == tenantId && x.VehicleClassId == existing.Id)
                        .Select(x => (DateOnly?)x.EffectiveDate).MaxAsync(ct);
                    if (latest is { } last && request.EffectiveDate <= last)
                        return Result<VehicleClassDto>.Failure(
                            "A new rate must take effect after the previous one. History is never re-priced.", ResultStatus.Conflict);
                }
                db.VehicleClassRates.Add(VehicleClassRate.Create(tenantId, existing.Id, request.EffectiveDate, request.Amount, actor));
                await db.SaveChangesAsync(ct);
                var current = request.EffectiveDate <= BusinessToday ? request.Amount : (decimal?)null;
                var listed = await GetAsync(ct);
                var dto = listed.Value!.First(x => x.Id == existing.Id);
                return Result<VehicleClassDto>.Success(dto with { CurrentAmount = dto.CurrentAmount ?? current });
            }
            catch (ArgumentException ex)
            {
                db.ChangeTracker.Clear();
                return Result<VehicleClassDto>.Failure(ex.Message, ResultStatus.Invalid);
            }
        }, ct);

    public Task<Result<VehicleClassDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        Run<VehicleClassDto>(async (tenantId, _, role) =>
        {
            if (role != "SuperAdmin") return Result<VehicleClassDto>.Forbidden();
            var row = await db.VehicleClasses.SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == id, ct);
            if (row is null) return Result<VehicleClassDto>.NotFound();
            row.SetActive(isActive);
            await db.SaveChangesAsync(ct);
            var listed = await GetAsync(ct);
            return Result<VehicleClassDto>.Success(listed.Value!.First(x => x.Id == id));
        }, ct);

    private async Task<Result<T>> Run<T>(Func<Guid, string, string, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        try { return await action(tenantId, currentUser.Username ?? "Office User", currentUser.Role!); }
        catch (DbUpdateException) { return Result<T>.Failure("The vehicle class changed concurrently. Reload and try again.", ResultStatus.Conflict); }
    }
}
