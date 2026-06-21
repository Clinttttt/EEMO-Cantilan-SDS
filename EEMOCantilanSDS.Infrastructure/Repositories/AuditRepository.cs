using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Audit;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// Read-only access to the immutable <c>AuditLog</c> table for the admin Audit Trail page.
/// All filtering, the action breakdown, and pagination run server-side. Actor usernames are
/// resolved to staff full names for display.
/// </summary>
public class AuditRepository(IAppDbContext context) : IAuditRepository
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
        var nameMap = await BuildStaffNameMapAsync(ct);

        // Base scope = every filter EXCEPT action, so the summary cards always show the full
        // Created/Updated/Deleted breakdown for the current search/date/actor/entity scope.
        var baseQuery = context.AuditLogs.AsNoTracking();

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
                a.Notes
            })
            .ToListAsync(ct);

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
                a.Notes))
            .ToList();

        // Filter-dropdown options only need (re)building on first load / filter change — not on
        // pure pagination — so skip the DISTINCT scans over the (growing) audit table when not asked.
        IReadOnlyList<AuditActorOptionDto> actorOptions = Array.Empty<AuditActorOptionDto>();
        IReadOnlyList<string> entityTypes = Array.Empty<string>();

        if (includeOptions)
        {
            var distinctActors = await context.AuditLogs.AsNoTracking()
                .Select(a => a.ActorName)
                .Distinct()
                .ToListAsync(ct);

            // Exclude payor self-service accounts (all-numeric mobile-number usernames); show staff names.
            actorOptions = distinctActors
                .Where(n => !IsNumericActor(n))
                .Select(n => new AuditActorOptionDto(n, Display(n, nameMap)))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            entityTypes = await context.AuditLogs.AsNoTracking()
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

    private async Task<Dictionary<string, string>> BuildStaffNameMapAsync(CancellationToken ct)
    {
        var admins = await context.AdminUsers.AsNoTracking()
            .Where(u => u.Username != null && u.FullName != null)
            .Select(u => new { u.Username, u.FullName })
            .ToListAsync(ct);

        var collectors = await context.CollectorUsers.AsNoTracking()
            .Where(u => u.Username != null && u.FullName != null)
            .Select(u => new { u.Username, u.FullName })
            .ToListAsync(ct);

        return admins.Concat(collectors)
            .GroupBy(u => u.Username!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().FullName!, StringComparer.OrdinalIgnoreCase);
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
