using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetAwaitingOr;

/// <summary>Staff reconciliation queue: online payments received but awaiting OR encoding.</summary>
public record GetOnlinePaymentsAwaitingOrQuery : IRequest<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>>;
