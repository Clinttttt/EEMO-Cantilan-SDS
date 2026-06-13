using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;

/// <summary>Staff encode the manually-issued Official Receipt number for an online payment that is
/// awaiting OR, completing the transaction.</summary>
public record IssueOnlinePaymentOrNumberCommand(Guid TransactionId, string ORNumber) : IRequest<Result<bool>>;
