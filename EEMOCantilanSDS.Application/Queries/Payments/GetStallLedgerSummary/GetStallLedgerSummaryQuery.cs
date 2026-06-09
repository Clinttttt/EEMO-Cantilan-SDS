using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallLedgerSummary;

public record GetStallLedgerSummaryQuery(Guid StallId) : IRequest<Result<StallLedgerSummaryDto>>;
