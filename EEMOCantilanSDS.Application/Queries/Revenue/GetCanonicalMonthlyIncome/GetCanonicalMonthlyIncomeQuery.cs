using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;

/// <summary>
/// Canonical classified cash for one year (Jan–Dec) or one month, by Collection business date. AsOf requires an
/// explicit past cutoff; LatestCorrected must not supply one — the server captures its own cutoff.
/// </summary>
public sealed record GetCanonicalMonthlyIncomeQuery(
    int Year,
    int? Month,
    CanonicalReportingBasis Basis,
    DateTimeOffset? AsOf = null) : IRequest<Result<CanonicalMonthlyIncomeDto>>;
