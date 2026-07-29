using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories;

/// <summary>
/// Replaces the signatory lines the CURRENT tenant prints at the foot of its official sheets.
/// <paramref name="Signatories"/> is the ordered list; an empty list restores the office's default trio, so a
/// Head can always get the standard sheet back. Presentation only — no report figure depends on it.
/// </summary>
public record SetReportSignatoriesCommand(IReadOnlyList<ReportSignatoryDto> Signatories)
    : IRequest<Result<bool>>;

/// <summary>One signatory line: the caption above the rule ("Prepared by") and the name beneath it.</summary>
public record ReportSignatoryDto(string Caption, string Name);
