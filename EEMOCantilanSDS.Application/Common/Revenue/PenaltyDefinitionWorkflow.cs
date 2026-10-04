using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Approved penalty definitions and the register of posted fines (IA-049). The Head defines penalties; Head and Admin
/// read them and the register. A fine is collected only by adding a definition to an Official Receipt draft in the
/// Composer, never by typing a free-text charge, and the register is a read of the canonical posted lines.
/// </summary>
public sealed class PenaltyDefinitionWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync(CancellationToken ct = default) =>
        Run<IReadOnlyList<PenaltyDefinitionDto>>(async actor =>
        {
            var versions = await db.PenaltyDefinitions.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId).ToListAsync(ct);
            var today = BusinessToday;
            var current = versions.Select(x => x.Code).Distinct()
                .Select(code => PenaltyDefinition.Resolve(versions, code, today))
                .OfType<PenaltyDefinition>()
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(ToDto).ToList();
            return Result<IReadOnlyList<PenaltyDefinitionDto>>.Success(current);
        }, ct);

    public Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request, CancellationToken ct = default) =>
        Run<PenaltyDefinitionDto>(async actor =>
        {
            // Defining approved charges is the Head authority, like revenue classification policy.
            if (actor.Role != "SuperAdmin") return Result<PenaltyDefinitionDto>.Forbidden();
            if (request.EffectiveDate < new DateOnly(2020, 1, 1) || request.EffectiveDate > BusinessToday.AddDays(366))
                return Result<PenaltyDefinitionDto>.Failure("Choose a realistic effective date (not more than a year ahead).", ResultStatus.Invalid);
            PenaltyDefinition version;
            try
            {
                version = PenaltyDefinition.Create(actor.TenantId, (request.Code ?? string.Empty).Trim().ToUpperInvariant(),
                    request.EffectiveDate, request.DisplayName ?? string.Empty, request.AppliesTo, request.Basis,
                    request.FixedAmount, request.MaximumAmount, request.IsActive, actor.Username);
            }
            catch (ArgumentException ex)
            {
                return Result<PenaltyDefinitionDto>.Failure(ex.Message, ResultStatus.Invalid);
            }
            db.PenaltyDefinitions.Add(version);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return Result<PenaltyDefinitionDto>.Failure("The penalty definition changed concurrently. Reload and try again.", ResultStatus.Conflict);
            }
            return Result<PenaltyDefinitionDto>.Success(ToDto(version));
        }, ct);

    /// <summary>Posted fines in a period: what the canonical Collections recorded, with the OR each rode on.</summary>
    public Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Run<IReadOnlyList<PenaltyRegisterRowDto>>(async actor =>
        {
            if (from > to || to.DayNumber - from.DayNumber > 366)
                return Result<IReadOnlyList<PenaltyRegisterRowDto>>.Failure("Choose a valid period of no more than 367 days.", ResultStatus.Invalid);
            var rows = await (
                from collection in db.Collections.AsNoTracking()
                join line in db.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
                join version in db.PenaltyDefinitions.AsNoTracking()
                    on new { line.MunicipalityId, Id = line.SourceId } equals new { version.MunicipalityId, Id = (Guid?)version.Id }
                where collection.MunicipalityId == actor.TenantId
                    && collection.BusinessDate >= @from && collection.BusinessDate <= to
                    && line.SourceKind == CollectionSourceKind.PenaltyDefinition
                orderby collection.BusinessDate descending, collection.RecordedAtUtc descending
                select new { collection.Id, collection.BusinessDate, collection.RecordedAtUtc, collection.PayerName, collection.ReferenceCode,
                    version.Code, version.DisplayName, line.CalculationSnapshot, line.Amount }).ToListAsync(ct);
            var ids = rows.Select(x => x.Id).ToArray();
            var corrections = await db.CollectionCorrections.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && ids.Contains(x.OriginalCollectionId))
                .Select(x => new { x.OriginalCollectionId, x.FinancialEffectAmount }).ToListAsync(ct);
            var register = rows.Select(x => new PenaltyRegisterRowDto(x.Id, x.BusinessDate, x.RecordedAtUtc, x.PayerName,
                ReadOrigin(x.CalculationSnapshot), x.Code, x.DisplayName,
                x.ReferenceCode, x.Amount,
                corrections.Any(c => c.OriginalCollectionId == x.Id && c.FinancialEffectAmount < 0m) ? "Reversed" : "Posted")).ToList();
            return Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success(register);
        }, ct);

    /// <summary>The optional descriptive origin (for example a stall and month) recorded on the line; it prices nothing.</summary>
    public static string? ReadOrigin(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            return document.RootElement.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.String
                ? origin.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static PenaltyDefinitionDto ToDto(PenaltyDefinition x) => new(
        x.Id, x.Code, x.DisplayName, x.AppliesTo, x.Basis, x.FixedAmount, x.MaximumAmount, x.IsActive, x.EffectiveDate);

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
            return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        try { return await action(new Actor(userId, tenantId, currentUser.Username ?? "Office User", currentUser.Role!)); }
        catch (DbUpdateException) { return Result<T>.Failure("The operation conflicts with another saved transaction.", ResultStatus.Conflict); }
    }

    private sealed record Actor(Guid UserId, Guid TenantId, string Username, string Role);
}
