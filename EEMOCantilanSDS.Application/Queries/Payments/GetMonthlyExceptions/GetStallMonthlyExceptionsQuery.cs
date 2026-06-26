using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMonthlyExceptions;

/// <summary>Returns the excused billing months (1–12) for a monthly-rental stall in a given year.</summary>
public record GetStallMonthlyExceptionsQuery(Guid StallId, int Year) : IRequest<Result<IReadOnlyList<int>>>;
