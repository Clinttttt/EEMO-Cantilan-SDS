using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// Answers OR-number availability from the database, delegating to <see cref="OrNumberRegistry"/> — the one place that
/// knows which tables hold receipt numbers.
///
/// <para>
/// Named after its mechanism rather than its role, matching <c>IdentityPasswordHasher</c>: the port says what the question
/// is, the implementation says how it is answered. It shares the request's <see cref="AppDbContext"/>, so a number written
/// earlier in the same request is already visible to it.
/// </para>
/// </summary>
public sealed class DbOrNumberRegistry(AppDbContext context) : IOrNumberRegistry
{
    public Task<bool> IsAvailableAsync(string orNumber, CancellationToken ct = default) =>
        OrNumberRegistry.IsAvailableAsync(context, orNumber, ct);

    public Task<bool> IsAvailableForUtilityBillAsync(string orNumber, Guid? excludeBillId, CancellationToken ct = default) =>
        OrNumberRegistry.IsAvailableAsync(context, orNumber, ct, excludeUtilityBillId: excludeBillId);
}
