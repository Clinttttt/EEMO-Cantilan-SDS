using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Slaughterhouse;

public sealed record SaveApprovedSlaughterAnimalRequest(string AnimalName, decimal RatePerHead);

/// <summary>
/// The Head/Admin-approved custom slaughter animals and their per-head rates (IA-050). This is the only place a rate for an
/// unusual animal is created or changed; the collector selects from these and can never type a rate. A change applies to
/// new transactions: every recorded transaction keeps the rate it was charged, so history is never re-priced.
/// </summary>
public sealed class ApprovedSlaughterAnimalWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    ISlaughterAnimalLabelProvider labels)
{
    public Task<Result<SlaughterAnimalRateDto>> SaveAsync(SaveApprovedSlaughterAnimalRequest request, CancellationToken ct = default) =>
        Run(async (tenantId, actor) =>
        {
            var name = (request.AnimalName ?? string.Empty).Trim();
            if (name.Length is 0 or > 60)
                return Result<SlaughterAnimalRateDto>.Failure("Enter an animal name of 1-60 characters.", ResultStatus.Invalid);
            if (request.RatePerHead <= 0m || request.RatePerHead > 1_000_000m || decimal.Round(request.RatePerHead, 2) != request.RatePerHead)
                return Result<SlaughterAnimalRateDto>.Failure("The approved rate must be a positive amount in whole centavos.", ResultStatus.Invalid);
            var builtIn = await labels.GetAsync(ct);
            if (SlaughterAnimalNames.IsCanonicalNameOrAlias(name)
                || SlaughterAnimalNames.CollidesWithAny(name, [builtIn.Hog, builtIn.Carabao, builtIn.Cow]))
                return Result<SlaughterAnimalRateDto>.Failure("That is a built-in animal; its rate comes from the slaughterhouse rate configuration.", ResultStatus.Conflict);

            var existing = await db.SlaughterAnimalRates
                .Where(x => x.MunicipalityId == tenantId && x.AnimalName.ToLower() == name.ToLower()).SingleOrDefaultAsync(ct);
            if (existing is null)
            {
                existing = SlaughterAnimalRate.Create(name, request.RatePerHead, tenantId, actor);
                db.SlaughterAnimalRates.Add(existing);
            }
            else
            {
                existing.UpdateRate(request.RatePerHead, actor);
                existing.SetActive(true, actor);
            }
            await db.SaveChangesAsync(ct);
            return Result<SlaughterAnimalRateDto>.Success(
                new SlaughterAnimalRateDto(existing.Id, existing.AnimalName, existing.RatePerHead, existing.IsActive));
        }, ct);

    public Task<Result<SlaughterAnimalRateDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        Run(async (tenantId, actor) =>
        {
            var row = await db.SlaughterAnimalRates.SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == id, ct);
            if (row is null) return Result<SlaughterAnimalRateDto>.NotFound();
            row.SetActive(isActive, actor);
            await db.SaveChangesAsync(ct);
            return Result<SlaughterAnimalRateDto>.Success(new SlaughterAnimalRateDto(row.Id, row.AnimalName, row.RatePerHead, row.IsActive));
        }, ct);

    private async Task<Result<T>> Run<T>(Func<Guid, string, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        try { return await action(tenantId, currentUser.Username ?? "Office User"); }
        catch (DbUpdateException) { return Result<T>.Failure("The rate changed concurrently. Reload and try again.", ResultStatus.Conflict); }
    }
}
