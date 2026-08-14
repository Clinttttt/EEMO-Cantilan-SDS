using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Entry partial of CollectorRepository: the collector ACCOUNT repository (ICollectorRepository). The projections the
// collector's own app reads live in CollectorRepository.Mobile.cs, the office's collector reports in
// CollectorRepository.Reporting.cs, and the recognition and obligation arithmetic they share in
// CollectorRepository.Recognition.cs.
//
// One class in four files, not four classes: those projections reuse the same private money arithmetic the office's
// reports use, and duplicating money arithmetic is how two screens start disagreeing. What IS separate is the contracts —
// a handler serving the app cannot reach an authentication lookup.
public partial class CollectorRepository(AppDbContext context, IFeeRateResolver feeRateResolver, IClock clock)
    : ICollectorRepository, ICollectorMobileQueries, ICollectorReportingQueries
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public CollectorRepository(AppDbContext context) : this(context, new FeeRateResolver(context), new SystemClock()) { }

    /// <summary>
    /// Captured into fields because this class is PARTIAL: a primary-constructor parameter is only in scope in the file that
    /// declares it, and the reads that need the context, the rates and "today" are spread across the sibling files. The same
    /// convention <see cref="Reports.FacilityReportsRepository"/> already uses for the same reason.
    /// </summary>
    private readonly AppDbContext _context = context;

    private readonly IFeeRateResolver _feeRateResolver = feeRateResolver;
    private readonly IClock _clock = clock;

    // Current municipality's resolved NPM rates for the in-flight query; default to the ordinance
    // constants so Cantilan is byte-for-byte, refreshed per public method via LoadNpmRatesAsync.
    private decimal _npmDailyRate = FeeRates.NpmDailyFee;
    private decimal _npmFishRate = FeeRates.NpmFishFeePerKilo;
    // The LGU's stated monthly rent for a market space (0 = it has stated none, so a month is thirty of its days).
    private decimal _npmMonthlyRent;

    private async Task LoadNpmRatesAsync(DateOnly asOf, CancellationToken ct)
    {
        var snapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        _npmDailyRate = snapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        _npmFishRate = snapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
        _npmMonthlyRent = snapshot.Resolve(FeeRateKey.NpmMonthlyStall, asOf);
    }

    public async Task<CollectorUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveCollectorIdsByFacilityAsync(FacilityCode facilityCode, CancellationToken cancellationToken = default)
    {
        // Join assignments to ACTIVE collectors; the tenant query filter on both keeps this LGU-scoped.
        return await _context.CollectorFacilityAssignments
            .Where(a => a.FacilityCode == facilityCode)
            .Join(_context.CollectorUsers.Where(c => c.IsActive),
                  a => a.CollectorId, c => c.Id, (a, c) => c.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    // The occupant whose contract term COVERED the record's period — active-agnostic, since the lessee at
    // that time may since have been replaced (a historical record must show who paid then, not today's
    // occupant). Falls back to the most recent contract, then "—".
    public async Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _context.CollectorUsers
            .FirstOrDefaultAsync(c => c.Username == username, cancellationToken);
    }

    public async Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, CancellationToken cancellationToken = default)
    {
        var normalized = usernameOrEmployeeId.Trim();
        // Login derives the tenant from the user, so span every LGU (bypass the tenant filter) while still
        // excluding soft-deleted accounts. Subdomain-scoped login is the Phase-5 refinement.
        return await _context.CollectorUsers
            .IgnoreQueryFilters()
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c =>
                !c.IsDeleted && (c.Username == normalized || c.EmployeeId == normalized),
                cancellationToken);
    }

    public async Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, Guid municipalityId, CancellationToken cancellationToken = default)
    {
        var normalized = usernameOrEmployeeId.Trim();
        // Scoped login: the tenant is known up-front, so resolve the username/employee-id WITHIN that
        // municipality. Prevents a value shared across LGUs from resolving to the wrong tenant's account
        // (which would fail the password check against the wrong hash and block a legitimate collector).
        return await _context.CollectorUsers
            .IgnoreQueryFilters()
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c =>
                !c.IsDeleted && c.MunicipalityId == municipalityId
                && (c.Username == normalized || c.EmployeeId == normalized),
                cancellationToken);
    }

    public async Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default)
    {
        await _context.CollectorUsers.AddAsync(collector, cancellationToken);
    }

    public async Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        // Uniqueness must consider soft-deleted users too (their rows still exist), so bypass the global
        // filter — but scope to the caller's municipality when resolved (empty tenant → global fallback).
        var mid = _context.CurrentMunicipalityId;
        return !await _context.CollectorUsers.IgnoreQueryFilters().AnyAsync(c => (mid == Guid.Empty || c.MunicipalityId == mid) && c.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default)
    {
        var mid = _context.CurrentMunicipalityId;
        return !await _context.Users.IgnoreQueryFilters().AnyAsync(u => (mid == Guid.Empty || u.MunicipalityId == mid) && u.Username == username, cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default)
    {
        var mid = _context.CurrentMunicipalityId;
        return !await _context.Users.IgnoreQueryFilters().AnyAsync(u => (mid == Guid.Empty || u.MunicipalityId == mid) && u.Email == email, cancellationToken);
    }

    public async Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var facilities = await _context.Facilities
            .Where(f => facilityCodes.Contains(f.Code))
            .ToListAsync(cancellationToken);

        foreach (var facility in facilities)
        {
            var assignment = CollectorFacilityAssignment.Create(
                collectorId,
                facility.Id,
                facility.Code);

            await _context.CollectorFacilityAssignments.AddAsync(assignment, cancellationToken);
        }
    }

    public async Task ReplaceFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CollectorFacilityAssignments
            .Where(a => a.CollectorId == collectorId)
            .ToListAsync(cancellationToken);

        // Diff so unchanged assignments are left intact (avoids unique-index conflicts on re-add).
        _context.CollectorFacilityAssignments.RemoveRange(existing.Where(a => !facilityCodes.Contains(a.FacilityCode)));

        var existingCodes = existing.Select(a => a.FacilityCode).ToHashSet();
        var toAdd = facilityCodes.Where(c => !existingCodes.Contains(c)).ToList();
        await AddFacilityAssignmentsAsync(collectorId, toAdd, cancellationToken);
    }

    public async Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = _clock.PhilippineNow.Year;
        var mid = _context.CurrentMunicipalityId;

        // Per-LGU prefix from the tenant's own office acronym (Cantilan = "EEMO"); fallback keeps it
        // non-empty for a tenant that hasn't set one. Uppercased for a consistent ID format.
        var acronym = await _context.Municipalities
            .IgnoreQueryFilters()
            .Where(m => m.Id == mid)
            .Select(m => m.OfficeAcronym)
            .FirstOrDefaultAsync(cancellationToken);
        var prefixCode = string.IsNullOrWhiteSpace(acronym) ? "EMP" : acronym.Trim().ToUpperInvariant();
        var prefix = $"{prefixCode}-{currentYear}-";

        // Numbering is scoped to THIS municipality (employee IDs are unique per LGU), across soft-deleted
        // rows too. Cantilan was the only tenant, so its sequence is unchanged.
        var lastEmployeeId = await _context.CollectorUsers
            .IgnoreQueryFilters()
            .Where(c => (mid == Guid.Empty || c.MunicipalityId == mid) && c.EmployeeId!.StartsWith(prefix))
            .OrderByDescending(c => c.EmployeeId)
            .Select(c => c.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        int nextNumber = 1;
        if (lastEmployeeId != null)
        {
            var numberPart = lastEmployeeId.Replace(prefix, "");
            if (int.TryParse(numberPart, out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D3}";
    }

}
