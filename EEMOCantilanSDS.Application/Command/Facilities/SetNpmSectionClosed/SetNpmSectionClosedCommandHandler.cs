using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionClosed;

/// <summary>
/// Closes one of the office's own market sections along with the stalls in it, or reopens both.
/// </summary>
/// <remarks>
/// <para>
/// The office chose this deliberately, having been told what it means, so the one thing this handler must never do is
/// surprise it. Two properties carry that:
/// </para>
/// <para>
/// IT DOES NOT INVENT A FREEZE. Every stall is closed and reopened through
/// <see cref="ToggleStallStatusCommand"/>, the path the console's own per-stall control uses. That path drops a stall out
/// of the register on close, and on reopen writes its whole frozen span as excused, so a closure never back-bills as
/// arrears. Duplicating that arithmetic here would be a second rule for the same money, and the two would drift.
/// </para>
/// <para>
/// IT REMEMBERS WHAT IT TOUCHED. The closure row records the stalls this act closed, so reopening returns exactly those.
/// A stall the office had already closed for its own reasons is not in the list and stays closed - the alternative is
/// reopening somebody's space months later because a section was reopened, and nobody would know why.
/// </para>
/// <para>
/// Closing is idempotent: closing an already-closed section re-reads which stalls are still active and adds them, because
/// a stall may have been recorded in the section between the two acts.
/// </para>
/// </remarks>
public class SetNpmSectionClosedCommandHandler(
    IAppDbContext context,
    IFacilityRepository facilityRepo,
    IStallRepository stallRepository,
    ISender sender,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IClock clock) : IRequestHandler<SetNpmSectionClosedCommand, Result<int>>
{
    public async Task<Result<int>> Handle(SetNpmSectionClosedCommand request, CancellationToken ct)
    {
        var section = (request.Section ?? string.Empty).Trim();
        if (section.Length == 0)
            return Result<int>.Failure("Name the section being closed.", ResultStatus.Invalid);

        // Only a section the office has actually registered, under the name it registered, exactly as pricing one is.
        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<int>.NotFound();

        var registered = npm.CustomSectionNames
            .FirstOrDefault(n => string.Equals(n, section, StringComparison.OrdinalIgnoreCase));
        if (registered is null)
            return Result<int>.Failure($"{section} is not one of your market's sections.", ResultStatus.Invalid);

        var existing = await context.FacilitySectionClosures
            .FirstOrDefaultAsync(c => c.FacilityCode == FacilityCode.NPM && c.SectionName == registered, ct);

        var affected = request.Closed
            ? await CloseAsync(registered, existing, ct)
            : await ReopenAsync(registered, existing, ct);

        await context.SaveChangesAsync(ct);

        // The market's tabs, the stall register and the roster all read what is open, and a closed stall leaves the
        // register, so the same views a payment affects are the views this affects.
        var today = clock.PhilippineToday;
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, today.Year, today.Month, ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<int>.Success(affected);
    }

    /// <summary>Closes the section and every stall in it that is still open, and records which those were.</summary>
    private async Task<int> CloseAsync(string registered, FacilitySectionClosure? existing, CancellationToken ct)
    {
        var stalls = await stallRepository.GetStallsWithContractsByFacilityAsync(
            FacilityCode.NPM, section: null, customSectionName: registered, ct);

        var toClose = stalls.Where(s => s.IsActive()).Select(s => s.Id).ToList();

        foreach (var id in toClose)
        {
            var result = await sender.Send(new ToggleStallStatusCommand(id, Close: true), ct);
            if (!result.IsSuccess) return toClose.IndexOf(id);   // stop at the first refusal; the rest stay open
        }

        // A second closing keeps the stalls the first one closed AND adds any recorded since, so a reopen returns both.
        if (existing is not null)
            existing.Reclose(clock.PhilippineToday, existing.ClosedStallIds.Concat(toClose), "SectionClosed");
        else
            context.FacilitySectionClosures.Add(FacilitySectionClosure.Create(
                FacilityCode.NPM, registered, clock.PhilippineToday, toClose, createdBy: "SectionClosed"));

        return toClose.Count;
    }

    /// <summary>Reopens the section, and exactly the stalls this closure closed.</summary>
    private async Task<int> ReopenAsync(string registered, FacilitySectionClosure? existing, CancellationToken ct)
    {
        if (existing is null) return 0;   // not closed: nothing to undo, and nothing to report as changed

        var reopened = 0;
        foreach (var id in existing.ClosedStallIds)
        {
            var result = await sender.Send(new ToggleStallStatusCommand(id, Close: false), ct);
            if (result.IsSuccess) reopened++;
            // A stall since deleted or reassigned simply is not reopened. The section still opens: leaving it closed
            // because one of its spaces has gone would strand the whole section.
        }

        context.FacilitySectionClosures.Remove(existing);
        return reopened;
    }
}
