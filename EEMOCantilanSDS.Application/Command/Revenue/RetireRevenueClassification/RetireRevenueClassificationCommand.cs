using EEMOCantilanSDS.Application.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;

public sealed record RetireRevenueClassificationCommand(Guid ClassificationId) : IRequest<Result<bool>>;
