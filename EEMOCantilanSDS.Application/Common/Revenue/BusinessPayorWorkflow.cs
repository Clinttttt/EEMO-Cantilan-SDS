using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The office's controlled way to give an occupancy a stable Business Payor.
/// </summary>
/// <remarks>
/// <para>
/// An occupancy often exists only as occupant text with no Payor, so nothing collected against it can be found by Payor. Display names
/// are not identity: this workflow never links, merges or creates a Payor because two names look alike. Every link is the office's
/// explicit act on one named occupancy, written through <see cref="EEMOCantilanSDS.Domain.Entities.Facilities.Contract.AssociatePayor"/>.
/// </para>
/// <para>
/// Linking changes who the occupancy answers to from now on. It does not rewrite the occupant text, the contract, or any payment
/// already recorded: those stay as the evidence they were. An occupancy already linked to a Payor is never silently re-pointed.
/// </para>
/// </remarks>
public sealed class BusinessPayorWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality)
{
    private const int MaxRows = 300;

    /// <summary>Active occupancies, optionally only those that still need a Payor, with the linked Payor's name.</summary>
    public Task<Result<IReadOnlyList<PayorOccupancyDto>>> GetOccupanciesAsync(
        string? search, PayorLinkFilter filter, CancellationToken ct = default) =>
        Run<IReadOnlyList<PayorOccupancyDto>>(async actor =>
        {
            var query = db.Contracts.AsNoTracking()
                .Where(c => c.MunicipalityId == actor.TenantId && c.IsActive && c.Stall != null);
            query = filter switch
            {
                PayorLinkFilter.NeedsPayor => query.Where(c => c.PayorId == null),
                PayorLinkFilter.Linked => query.Where(c => c.PayorId != null),
                _ => query
            };
            var term = search?.Trim().ToLower();
            if (!string.IsNullOrEmpty(term))
                query = query.Where(c => c.ActualOccupant.ToLower().Contains(term)
                    || (c.NameOnContract != null && c.NameOnContract.ToLower().Contains(term))
                    || c.Stall!.StallNo.ToLower().Contains(term)
                    || (c.Payor != null && c.Payor.DisplayName.ToLower().Contains(term)));

            var rows = await query
                .OrderBy(c => c.Stall!.Facility!.ShortName).ThenBy(c => c.Stall!.StallNo)
                .Take(MaxRows)
                .Select(c => new PayorOccupancyDto(c.Id, c.StallId, c.Stall!.Facility!.ShortName, c.Stall.StallNo,
                    c.ActualOccupant, c.NameOnContract, c.PayorId, c.Payor != null ? c.Payor.DisplayName : null,
                    c.Stall.FacilityId, c.Stall.Facility!.Code, c.Stall.Facility.Name, c.Arrangement))
                .ToListAsync(ct);
            return Result<IReadOnlyList<PayorOccupancyDto>>.Success(rows);
        }, ct);

    /// <summary>Existing Business Payors matching the term, each with where it is already used.</summary>
    public Task<Result<IReadOnlyList<PayorCandidateDto>>> SearchPayorsAsync(string? search, CancellationToken ct = default) =>
        Run<IReadOnlyList<PayorCandidateDto>>(async actor =>
        {
            var payors = await BusinessPayorSearch.Apply(db.Payors.AsNoTracking(), actor.TenantId, search).ToListAsync(ct);
            var ids = payors.Select(p => p.Id).ToList();
            var occupancies = await db.Contracts.AsNoTracking()
                .Where(c => c.MunicipalityId == actor.TenantId && c.PayorId != null && ids.Contains(c.PayorId.Value))
                .Select(c => new { PayorId = c.PayorId!.Value, Facility = c.Stall!.Facility!.ShortName, c.Stall.StallNo })
                .ToListAsync(ct);
            var accounts = await db.ObligationAccounts.AsNoTracking()
                .Where(a => a.MunicipalityId == actor.TenantId && a.PayorId.HasValue && ids.Contains(a.PayorId.Value))
                .Select(a => new { a.PayorId, a.Kind, a.SubjectLabel })
                .ToListAsync(ct);
            var candidates = payors.Select(p => new PayorCandidateDto(p.Id, p.DisplayName, p.Kind,
                occupancies.Where(o => o.PayorId == p.Id).Select(o => $"{o.Facility} · {o.StallNo}")
                    .Concat(accounts.Where(a => a.PayorId == p.Id).Select(a => $"{ObligationCollectionSource.KindLabel(a.Kind)} · {a.SubjectLabel}"))
                    .Distinct().ToList())).ToList();
            return Result<IReadOnlyList<PayorCandidateDto>>.Success(candidates);
        }, ct);

    /// <summary>Links one occupancy to one existing Business Payor. Repeating the same link is a no-op; a different one is refused.</summary>
    public Task<Result<PayorLinkOutcomeDto>> LinkAsync(LinkPayorRequest request, CancellationToken ct = default) =>
        Run<PayorLinkOutcomeDto>(async actor =>
        {
            var contract = await db.Contracts.SingleOrDefaultAsync(c => c.MunicipalityId == actor.TenantId && c.Id == request.ContractId, ct);
            if (contract is null) return Result<PayorLinkOutcomeDto>.NotFound();
            var payor = await db.Payors.AsNoTracking().SingleOrDefaultAsync(p => p.MunicipalityId == actor.TenantId && p.Id == request.PayorId, ct);
            if (payor is null) return Result<PayorLinkOutcomeDto>.NotFound();
            if (contract.PayorId == payor.Id)
                return Result<PayorLinkOutcomeDto>.Success(new(contract.Id, payor.Id, payor.DisplayName, false));
            if (!contract.IsActive)
                return Result<PayorLinkOutcomeDto>.Failure("This occupancy is closed.", ResultStatus.Conflict);
            if (contract.PayorId is not null)
                return Result<PayorLinkOutcomeDto>.Failure("This occupancy is already linked to a Business Payor.", ResultStatus.Conflict);
            contract.AssociatePayor(payor.Id, actor.Username);
            await db.SaveChangesAsync(ct);
            return Result<PayorLinkOutcomeDto>.Success(new(contract.Id, payor.Id, payor.DisplayName, false));
        }, ct);

    /// <summary>Creates a Business Payor for one occupancy and links it, only on the office's explicit request.</summary>
    public Task<Result<PayorLinkOutcomeDto>> CreateAndLinkAsync(CreatePayorAndLinkRequest request, CancellationToken ct = default) =>
        Run<PayorLinkOutcomeDto>(async actor =>
        {
            var name = request.DisplayName?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > 200)
                return Result<PayorLinkOutcomeDto>.Failure("A name of at most 200 characters is required.", ResultStatus.Invalid);
            if (!Enum.IsDefined(request.Kind))
                return Result<PayorLinkOutcomeDto>.Failure("Choose Person or Organization.", ResultStatus.Invalid);
            var contract = await db.Contracts.SingleOrDefaultAsync(c => c.MunicipalityId == actor.TenantId && c.Id == request.ContractId, ct);
            if (contract is null) return Result<PayorLinkOutcomeDto>.NotFound();
            if (!contract.IsActive)
                return Result<PayorLinkOutcomeDto>.Failure("This occupancy is closed.", ResultStatus.Conflict);
            if (contract.PayorId is not null)
                return Result<PayorLinkOutcomeDto>.Failure("This occupancy is already linked to a Business Payor.", ResultStatus.Conflict);

            var lowered = name.ToLower();
            if (!request.ConfirmDuplicate && await db.Payors.AsNoTracking().AnyAsync(
                    p => p.MunicipalityId == actor.TenantId && p.DisplayName.ToLower() == lowered, ct))
                return Result<PayorLinkOutcomeDto>.Failure(
                    $"DUPLICATE_PAYOR: A Business Payor named {name} already exists. Link it, or confirm this is a different person.",
                    ResultStatus.Conflict);

            var payor = Payor.Create(actor.TenantId, name, request.Kind, actor.Username);
            db.Payors.Add(payor);
            contract.AssociatePayor(payor.Id, actor.Username);
            await db.SaveChangesAsync(ct);
            return Result<PayorLinkOutcomeDto>.Success(new(contract.Id, payor.Id, payor.DisplayName, true));
        }, ct);

    /// <summary>
    /// Creates a Business Payor on the office's explicit request, not tied to any occupancy (an account or import row links it itself).
    /// A same-named Payor is never reused silently: the request is refused unless the office confirms it is a different person.
    /// </summary>
    public Task<Result<PayorCandidateDto>> CreatePayorAsync(CreatePayorRequest request, CancellationToken ct = default) =>
        Run<PayorCandidateDto>(async actor =>
        {
            var name = request.DisplayName?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > 200)
                return Result<PayorCandidateDto>.Failure("A name of at most 200 characters is required.", ResultStatus.Invalid);
            if (!Enum.IsDefined(request.Kind))
                return Result<PayorCandidateDto>.Failure("Choose Person or Organization.", ResultStatus.Invalid);
            var lowered = name.ToLower();
            if (!request.ConfirmDuplicate && await db.Payors.AsNoTracking().AnyAsync(
                    p => p.MunicipalityId == actor.TenantId && p.DisplayName.ToLower() == lowered, ct))
                return Result<PayorCandidateDto>.Failure(
                    $"DUPLICATE_PAYOR: A Business Payor named {name} already exists. Use it, or confirm this is a different person.",
                    ResultStatus.Conflict);
            var payor = Payor.Create(actor.TenantId, name, request.Kind, actor.Username);
            db.Payors.Add(payor);
            await db.SaveChangesAsync(ct);
            return Result<PayorCandidateDto>.Success(new(payor.Id, payor.DisplayName, payor.Kind, []));
        }, ct);

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        return await action(new Actor(tenantId, currentUser.Username ?? "Office User"));
    }

    private sealed record Actor(Guid TenantId, string Username);
}
