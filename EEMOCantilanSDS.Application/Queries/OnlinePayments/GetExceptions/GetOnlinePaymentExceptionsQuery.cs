using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetExceptions;

/// <summary>The office's work list of online payments that did not simply complete.</summary>
public record GetOnlinePaymentExceptionsQuery : IRequest<Result<IReadOnlyList<OnlinePaymentExceptionDto>>>;
