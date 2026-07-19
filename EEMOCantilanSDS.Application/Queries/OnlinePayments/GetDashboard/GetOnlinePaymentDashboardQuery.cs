using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetDashboard;

/// <summary>Treasury overview + recent history for the admin Online Payments page (current LGU).</summary>
public record GetOnlinePaymentDashboardQuery : IRequest<Result<OnlinePaymentDashboardDto>>;
