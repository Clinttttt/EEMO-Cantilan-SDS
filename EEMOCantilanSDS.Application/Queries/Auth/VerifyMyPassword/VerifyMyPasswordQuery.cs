using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.VerifyMyPassword;

/// <summary>Re-authentication check: verifies the CURRENT user's own password before a sensitive action
/// (e.g. changing the online-payment account). Returns whether the password matched.</summary>
public record VerifyMyPasswordQuery(string Password) : IRequest<Result<bool>>;
