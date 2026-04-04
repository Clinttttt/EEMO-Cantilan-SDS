using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;

public record SaveOrNumberCommand(Guid PaymentId, string ORNumber) : IRequest<Result<bool>>;
