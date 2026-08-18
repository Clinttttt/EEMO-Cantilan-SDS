using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories;

/// <summary>
/// Stores the caller LGU's signatory lines. Tenant-scoped: a Head can only change their own municipality's
/// sheets. The list is normalised here — trimmed, blank lines dropped, and capped — so a malformed payload
/// can never produce a sheet the office cannot read.
///
/// <para>
/// <c>Signatories</c> is nullable and the two empty cases mean different things: <b>null</b> restores the office's
/// default trio, an <b>empty list</b> means the office wants no signatory lines at all. Both are legitimate; conflating
/// them is what made "remove the last line" put three back.
/// </para>
/// </summary>
public class SetReportSignatoriesCommandHandler(
    IMunicipalityRepository municipalityRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<SetReportSignatoriesCommand, Result<bool>>
{
    /// <summary>A signature strip is a row on paper; more than six lines cannot be read across a sheet.</summary>
    public const int MaxSignatories = 6;
    public const int MaxLength = 60;

    public async Task<Result<bool>> Handle(SetReportSignatoriesCommand request, CancellationToken ct)
    {
        var municipality = await municipalityRepository.GetByIdentifierAsync(tenantContext.TenantCode, ct);
        if (municipality is null) return Result<bool>.NotFound();

        var cleaned = (request.Signatories ?? Array.Empty<ReportSignatoryDto>())
            .Select(s => new ReportSignatoryDto(
                (s.Caption ?? string.Empty).Trim(),
                (s.Name ?? string.Empty).Trim()))
            .Where(s => s.Caption.Length > 0 || s.Name.Length > 0)
            .Take(MaxSignatories)
            .ToList();

        if (cleaned.Any(s => s.Caption.Length > MaxLength || s.Name.Length > MaxLength))
            return Result<bool>.Failure($"A signatory caption or name may be at most {MaxLength} characters.", ResultStatus.Invalid);

        // Three states, not two, and the difference matters on paper:
        //
        //   Signatories == null  ->  "use the office's default trio", stored as null.
        //   Signatories == []    ->  "no signatory lines at all", stored as an empty array.
        //   a populated list     ->  those lines.
        //
        // Until this distinction existed, removing the last line meant "not customised", so the default trio came
        // straight back and an office that wanted a sheet with no footer could not have one. Null and "[]" are both
        // unambiguous in the database; what was ambiguous was using one value for two intentions.
        var json = request.Signatories is null
            ? null
            : JsonSerializer.Serialize(new StoredSignatories(
                string.Equals(request.Align, "center", StringComparison.OrdinalIgnoreCase) ? "center" : "left",
                cleaned));

        municipality.SetReportSignatories(json, currentUser.Username ?? "Head");
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// What is written to the column. An object rather than a bare array so the alignment travels with the lines.
    /// Values stored before alignment existed are bare arrays, and the reader still understands those as "left" — an
    /// office that never touches this keeps exactly the footer it prints today.
    /// </summary>
    private sealed record StoredSignatories(string Align, IReadOnlyList<ReportSignatoryDto> Lines);
}
