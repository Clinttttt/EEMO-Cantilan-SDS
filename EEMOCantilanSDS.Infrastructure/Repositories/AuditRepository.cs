using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Audit;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// Read-only access to the immutable <c>AuditLog</c> table for the admin Audit Trail page.
/// All filtering, the action breakdown, and pagination run server-side. Actor usernames are
/// resolved to staff full names for display.
/// </summary>
/// <param name="municipality">
/// The caller's LGU, used to keep another municipality's staff out of this office's trail. Optional so the
/// repository can still be constructed with a bare context (tests, token-less paths); when absent the tenant is
/// treated as unresolved and behaviour is exactly as it was.
/// </param>
public class AuditRepository(IAppDbContext context, ICurrentMunicipalityAccessor? municipality = null) : IAuditRepository
{
    public async Task<AuditTrailDto> GetAuditTrailAsync(
        string? search,
        string? action,
        string? entityType,
        string? actor,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        bool includeOptions,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 25 : pageSize;

        // Resolve usernames -> staff full names up front (admins + collectors; payors excluded).
        // Needed both to display names and to let the search box match what the user sees.
        var staff = await BuildStaffDirectoryAsync(ct);
        var nameMap = staff.ToDictionary(s => s.Key, s => s.Value.FullName, StringComparer.OrdinalIgnoreCase);

        // Actors that belong to ANOTHER municipality never appear in this LGU's trail. Audit rows are stamped
        // with the tenant that was current when they were written, and some are written in flows where that is
        // the default tenant — a platform operator creating another LGU's Head, or a token-less auth step. The
        // office's own audit page must show its own people, so those rows are excluded here by the actor's
        // municipality rather than by the row's stamp. Accounts we cannot attribute (deleted staff, payors,
        // "system") are kept: an audit trail may never silently drop an event.
        var currentMunicipality = municipality?.MunicipalityId ?? Guid.Empty;
        var foreignActors = currentMunicipality == Guid.Empty
            ? new List<string>()
            : staff.Where(s => s.Value.MunicipalityId != Guid.Empty && s.Value.MunicipalityId != currentMunicipality)
                   // Lower-cased on both sides: usernames are matched case-insensitively everywhere else in the
                   // system, and a differently-cased actor name must not slip past this exclusion.
                   .Select(s => s.Key.ToLower())
                   .ToList();

        // Base scope = every filter EXCEPT action, so the summary cards always show the full
        // Created/Updated/Deleted breakdown for the current search/date/actor/entity scope.
        var baseQuery = context.AuditLogs.AsNoTracking();

        if (foreignActors.Count > 0)
            baseQuery = baseQuery.Where(a => !foreignActors.Contains(a.ActorName.ToLower()));

        // Incoming bounds carry the correct UTC instant but may bind with Kind=Unspecified over the
        // query string; Npgsql requires Kind=Utc for the 'timestamp with time zone' column.
        if (fromUtc.HasValue)
        {
            var from = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            baseQuery = baseQuery.Where(a => a.LoggedAt >= from);
        }
        if (toUtc.HasValue)
        {
            var to = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            baseQuery = baseQuery.Where(a => a.LoggedAt <= to);
        }
        if (!string.IsNullOrWhiteSpace(entityType))
            baseQuery = baseQuery.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var actorTerm = actor.Trim().ToLower();
            baseQuery = baseQuery.Where(a => a.ActorName.ToLower().Contains(actorTerm));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            // Users search by what they SEE (the resolved full name), but the table stores the
            // username. Map staff whose full name matches the term back to their usernames.
            var matchedUsernames = nameMap
                .Where(kv => kv.Value.ToLower().Contains(term))
                .Select(kv => kv.Key)
                .ToList();

            baseQuery = baseQuery.Where(a =>
                a.ActorName.ToLower().Contains(term) ||
                a.EntityType.ToLower().Contains(term) ||
                a.Action.ToLower().Contains(term) ||
                (a.Notes != null && a.Notes.ToLower().Contains(term)) ||
                matchedUsernames.Contains(a.ActorName));
        }

        // Action breakdown over the base scope (single round-trip via GROUP BY).
        var breakdown = await baseQuery
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountFor(string a) => breakdown.FirstOrDefault(b => b.Action == a)?.Count ?? 0;
        var createdCount = CountFor("Created");
        var updatedCount = CountFor("Updated");
        var deletedCount = CountFor("Deleted");
        var totalEvents = breakdown.Sum(b => b.Count);   // real total (not just the 3 known actions)

        // Action filter applies only to the listed page + its pagination total.
        var listQuery = baseQuery;
        if (!string.IsNullOrWhiteSpace(action))
            listQuery = listQuery.Where(a => a.Action == action);

        var totalCount = await listQuery.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var rawItems = await listQuery
            .OrderByDescending(a => a.LoggedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.LoggedAt,
                a.ActorName,
                a.ActorRole,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Notes,
                a.OldValues,
                a.NewValues
            })
            .ToListAsync(ct);

        // Related records this page refers to, loaded once so each row can be described in words.
        var stallIds = new HashSet<Guid>();
        var personIds = new HashSet<Guid>();
        foreach (var a in rawItems)
            AuditDetailComposer.CollectReferences(a.EntityType, a.EntityId, a.NewValues, a.OldValues, stallIds, personIds);

        var lookup = await BuildLookupAsync(stallIds, personIds, ct);

        var items = rawItems
            .Select(a => new AuditLogDto(
                a.Id,
                a.LoggedAt,
                a.ActorName,
                Display(a.ActorName, nameMap),
                a.ActorRole,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Notes,
                // A stored note (when a handler wrote one) is the most specific description there is; otherwise
                // the event is described from its own snapshot.
                string.IsNullOrWhiteSpace(a.Notes)
                    ? AuditDetailComposer.Describe(a.Action, a.EntityType, a.EntityId, a.NewValues, a.OldValues, lookup)
                    : a.Notes!,
                AuditDetailComposer.Changes(a.OldValues, a.NewValues)))
            .ToList();

        // Filter-dropdown options only need (re)building on first load / filter change — not on
        // pure pagination — so skip the DISTINCT scans over the (growing) audit table when not asked.
        IReadOnlyList<AuditActorOptionDto> actorOptions = Array.Empty<AuditActorOptionDto>();
        IReadOnlyList<string> entityTypes = Array.Empty<string>();

        if (includeOptions)
        {
            // Built from the SAME scoped query as the trail, so the dropdown can only offer actors whose events
            // this office is actually allowed to read.
            var distinctActors = await baseQuery
                .Select(a => a.ActorName)
                .Distinct()
                .ToListAsync(ct);

            // Exclude payor self-service accounts (all-numeric mobile-number usernames); show staff names.
            actorOptions = distinctActors
                .Where(n => !IsNumericActor(n))
                .Select(n => new AuditActorOptionDto(n, Display(n, nameMap)))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            entityTypes = await baseQuery
                .Select(a => a.EntityType)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync(ct);
        }

        return new AuditTrailDto(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            totalEvents,
            createdCount,
            updatedCount,
            deletedCount,
            actorOptions,
            entityTypes);
    }

    /// <summary>
    /// Loads the stalls and people a page of audit rows refers to, in one round-trip each. Tenant filters apply
    /// as usual here: a record this office may not read simply goes unnamed, and the sentence omits it.
    /// </summary>
    private async Task<AuditDetailComposer.Lookup> BuildLookupAsync(
        HashSet<Guid> stallIds, HashSet<Guid> personIds, CancellationToken ct)
    {
        var stalls = new Dictionary<Guid, AuditDetailComposer.StallRef>();
        var people = new Dictionary<Guid, string>();

        if (stallIds.Count > 0)
        {
            var rows = await context.Stalls.AsNoTracking()
                .Where(s => stallIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    s.StallNo,
                    FacilityName = s.Facility!.Name,
                    s.Section,
                    s.CustomSectionName,
                    VegetableLabel = s.Facility!.VegetableSectionLabel,
                    FishLabel = s.Facility!.FishSectionLabel,
                    MeatLabel = s.Facility!.MeatSectionLabel,
                    Occupant = s.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault()
                })
                .ToListAsync(ct);

            foreach (var r in rows)
            {
                // The tenant's own section label when it has one, else the canonical name, else the stall's
                // custom section — the same precedence the rosters use.
                var section = r.Section switch
                {
                    MarketSection.VegetableArea => r.VegetableLabel ?? GetSectionName(r.Section),
                    MarketSection.FishSection => r.FishLabel ?? GetSectionName(r.Section),
                    MarketSection.MeatSection => r.MeatLabel ?? GetSectionName(r.Section),
                    _ => r.CustomSectionName
                };

                stalls[r.Id] = new AuditDetailComposer.StallRef(r.StallNo, r.FacilityName, section, r.Occupant);
            }
        }

        if (personIds.Count > 0)
        {
            var payors = await context.PayorUsers.AsNoTracking()
                .Where(p => personIds.Contains(p.Id))
                .Select(p => new { p.Id, p.FullName })
                .ToListAsync(ct);

            foreach (var p in payors)
                if (!string.IsNullOrWhiteSpace(p.FullName))
                    people[p.Id] = p.FullName!;

            var vendors = await context.TpmVendors.AsNoTracking()
                .Where(v => personIds.Contains(v.Id))
                .Select(v => new { v.Id, v.VendorName })
                .ToListAsync(ct);

            foreach (var v in vendors)
                if (!string.IsNullOrWhiteSpace(v.VendorName))
                    people[v.Id] = v.VendorName!;
        }

        return new AuditDetailComposer.Lookup(stalls, people, new Dictionary<Guid, string>());
    }

    private static string? GetSectionName(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => null
    };

    private async Task<Dictionary<string, (string FullName, Guid MunicipalityId)>> BuildStaffDirectoryAsync(CancellationToken ct)
    {
        // Deliberately ignores the tenant query filter: this directory is what tells us WHICH municipality an
        // actor belongs to, so it has to be able to see accounts outside the current one. Nothing from another
        // municipality is ever returned to the caller — the directory is only used to exclude foreign actors and
        // to resolve this LGU's own names.
        var admins = await context.AdminUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Username != null && u.FullName != null)
            .Select(u => new { u.Username, u.FullName, u.MunicipalityId })
            .ToListAsync(ct);

        var collectors = await context.CollectorUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Username != null && u.FullName != null)
            .Select(u => new { u.Username, u.FullName, u.MunicipalityId })
            .ToListAsync(ct);

        return admins.Concat(collectors)
            .GroupBy(u => u.Username!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (g.First().FullName!, g.First().MunicipalityId),
                StringComparer.OrdinalIgnoreCase);
    }

    // Maps a stored actor username to a display name: "system" -> "System", known staff -> full name,
    // otherwise the username unchanged (e.g. a payor mobile number).
    private static string Display(string actorName, IReadOnlyDictionary<string, string> nameMap)
    {
        if (string.Equals(actorName, "system", StringComparison.OrdinalIgnoreCase))
            return "System";
        return nameMap.TryGetValue(actorName, out var fullName) && !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : actorName;
    }

    // Payor logins use their mobile number as username; treat an all-digit name as a payor account.
    private static bool IsNumericActor(string name) =>
        !string.IsNullOrEmpty(name) && name.All(char.IsDigit);
}
