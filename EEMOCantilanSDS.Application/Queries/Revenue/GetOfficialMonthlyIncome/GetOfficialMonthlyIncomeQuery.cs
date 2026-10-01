using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;

/// <summary>
/// The official Monthly Income for one year (Jan-Dec) or one selected month: authoritative legacy pre-cutover cash plus
/// authoritative canonical post-cutover cash, exactly once, in the office statement's rows (IA-051).
/// </summary>
public sealed record GetOfficialMonthlyIncomeQuery(int Year, int? Month) : IRequest<Result<OfficialMonthlyIncomeDto>>;
