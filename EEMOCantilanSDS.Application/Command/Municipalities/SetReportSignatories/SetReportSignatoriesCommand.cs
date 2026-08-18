using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories;

/// <summary>
/// Replaces the signatory lines the CURRENT tenant prints at the foot of its official sheets, and where they sit.
/// Presentation only — no report figure depends on it.
///
/// <para>
/// <paramref name="Signatories"/> carries THREE intentions, and the two empty ones are not the same:
/// </para>
///
/// <list type="bullet">
///   <item><b>null</b> — restore the office's default trio, so a Head can always get the standard sheet back.</item>
///   <item><b>an empty list</b> — print no signatory lines at all, which is a sheet some offices want.</item>
///   <item><b>a populated list</b> — those lines, in that order.</item>
/// </list>
///
/// <para>
/// They used to share one value, which is why removing the last line put three back and an office could not choose to
/// have none.
/// </para>
/// </summary>
/// <param name="Align">
/// "center" to centre the strip on the sheet; anything else, including null, leaves it as it has always been. Stored
/// alongside the lines rather than in a column of its own, so one save writes one value and the two cannot disagree.
/// </param>
public record SetReportSignatoriesCommand(
    IReadOnlyList<ReportSignatoryDto>? Signatories,
    string? Align = null)
    : IRequest<Result<bool>>;

/// <summary>One signatory line: the caption above the rule ("Prepared by") and the name beneath it.</summary>
public record ReportSignatoryDto(string Caption, string Name);
