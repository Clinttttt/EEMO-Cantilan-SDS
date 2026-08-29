using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetMyPayorProfile;

/// <summary>
/// The signed-in payor's own name and registered number.
///
/// <para>
/// Carries no id: the subject is whoever is asking, taken from the token. It exists because the payor's own portal holds
/// no token it can read — the session is HttpOnly cookies by design — so it cannot show a payor their own details without
/// asking for them. Both are the OFFICE's record: the name comes from the register, which is why activation stopped asking
/// a payor to type one.
/// </para>
/// </summary>
public record GetMyPayorProfileQuery : IRequest<Result<PayorProfileDto>>;

/// <param name="FullName">The name the office holds for this payor.</param>
/// <param name="ContactNumber">The registered number, which is also how the payor signs in.</param>
public record PayorProfileDto(string FullName, string ContactNumber);
