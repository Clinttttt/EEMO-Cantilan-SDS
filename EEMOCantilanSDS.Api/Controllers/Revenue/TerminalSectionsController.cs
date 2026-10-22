using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>Stable, tenant-owned Terminal sections. Head edits never authorize a Cash Ticket policy.</summary>
[ApiController]
[Route("api/terminal-sections")]
public sealed class TerminalSectionsController(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality) : ControllerBase
{


    // Refuse a missing or mismatched authenticated tenant before the global database filter is queried.
    private Guid Tenant => user.MunicipalityId is { } claim && claim != Guid.Empty
        && claim == municipality.MunicipalityId ? claim : Guid.Empty;

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        if (!user.IsAuthenticated || Tenant == Guid.Empty) return Forbid();
        return Ok(await TerminalSectionSettingsStore.ReadAsync(db, Tenant, ct));
    }

    [HttpPut]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> UpdateAsync([FromBody] EEMOCantilanSDS.Application.Dtos.Settings.TerminalSectionUpdateRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.Role != "SuperAdmin" || Tenant == Guid.Empty) return Forbid();
        if (!Enum.IsDefined(request.Section) || request.ExpectedVersion < 0)
            return BadRequest("Invalid Terminal section.");
        string? name;
        try { name = TerminalSectionSetting.NormalizeName(request.LocalDisplayName); }
        catch (ArgumentException) { return BadRequest("The local section name must be 80 characters or fewer."); }

        var rows = await db.TerminalSectionSettings.Where(x => x.MunicipalityId == Tenant).ToListAsync(ct);
        var adoptingLegacy = rows.Count == 0;
        if (adoptingLegacy)
        {
            // Old LGUs adopt their existing all-enabled state on their first Head edit.
            if (request.ExpectedVersion != 0) return Conflict("Reload the Terminal section settings.");
            rows = Enum.GetValues<TerminalSection>().Select(x =>
                TerminalSectionSetting.Create(x, true, null, Tenant, user.Username ?? "Head")).ToList();
            db.TerminalSectionSettings.AddRange(rows);
        }
        var selected = rows.SingleOrDefault(x => x.Section == request.Section);
        if (selected is null || (adoptingLegacy ? request.ExpectedVersion != 0 : selected.Version != request.ExpectedVersion))
            return Conflict("The Terminal settings were changed. Reload and try again.");
        var duplicate = rows.Any(x => x.Section != request.Section && (x.LocalDisplayName ??
            OfficeCollectionWorkflow.SectionName(x.Section)).Equals(
                name ?? OfficeCollectionWorkflow.SectionName(request.Section), StringComparison.OrdinalIgnoreCase));
        if (duplicate) return BadRequest("Two Terminal sections cannot have the same display name.");
        selected.Update(request.IsEnabled, name, user.Username ?? "Head");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict("The Terminal settings changed concurrently."); }
        catch (DbUpdateException) { return Conflict("Could not save the Terminal settings. Reload and try again."); }
        return Ok(await TerminalSectionSettingsStore.ReadAsync(db, Tenant, ct));
    }
}
