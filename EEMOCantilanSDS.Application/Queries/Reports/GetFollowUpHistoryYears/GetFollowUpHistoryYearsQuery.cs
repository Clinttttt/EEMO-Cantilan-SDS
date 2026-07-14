using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistoryYears;

/// <summary>
/// Years selectable on the Follow-up History page: every calendar year that has data, newest first
/// (current year down to the earliest activity year). Lets a back-dated prior-year settlement be
/// reached instead of being stranded outside the last-12-months window.
/// </summary>
public record GetFollowUpHistoryYearsQuery : IRequest<Result<IReadOnlyList<int>>>;
