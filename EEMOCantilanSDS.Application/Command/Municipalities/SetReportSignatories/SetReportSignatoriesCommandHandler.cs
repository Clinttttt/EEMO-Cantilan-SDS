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
/// can never produce a sheet the office cannot read, and an empty list clears the value back to the default.
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

        // No lines at all = "use the office's default trio", stored as null rather than an empty array so the
        // meaning is unambiguous in the database.
        var json = cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned);

        municipality.SetReportSignatories(json, currentUser.Username ?? "Head");
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
