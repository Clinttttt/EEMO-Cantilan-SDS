using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IPayorRepository
{
    /// <summary>Looks up a payor by their login identifier (registered contact number).</summary>
    Task<PayorUser?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default);

    /// <summary>Fetches an unused activation code by its value (null if not found).</summary>
    Task<PayorActivationCode?> GetActivationCodeAsync(string code, CancellationToken ct = default);

    /// <summary>True if any activation code already uses this value (for collision-free generation).</summary>
    Task<bool> ActivationCodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// True if a still-redeemable (unused, unexpired) activation code already exists for this contact
    /// number bound to a DIFFERENT stall. Guards against issuing two codes under one number for two
    /// different stalls/occupants, which would otherwise merge unrelated payors on activation.
    /// </summary>
    Task<bool> ActiveCodeExistsForContactOnOtherStallAsync(string contactNumber, Guid stallId, CancellationToken ct = default);

    /// <summary>Voids any still-redeemable code for the stall so only the newest one is active.</summary>
    Task RevokeActiveCodesForStallAsync(Guid stallId, string revokedBy, CancellationToken ct = default);

    Task AddActivationCodeAsync(PayorActivationCode code, CancellationToken ct = default);

    /// <summary>True when the stall is already linked to the given payor.</summary>
    Task<bool> LinkExistsAsync(Guid payorUserId, Guid stallId, CancellationToken ct = default);

    Task AddPayorAsync(PayorUser payor, CancellationToken ct = default);

    Task AddStallLinkAsync(PayorStallLink link, CancellationToken ct = default);

    /// <summary>Outstanding balances for every stall linked to the payor (read-only dashboard).</summary>
    Task<IReadOnlyList<PayorStallBalanceDto>> GetBalancesAsync(Guid payorUserId, CancellationToken ct = default);

    /// <summary>Per-record payable items (unpaid/partial months) for the payor's linked stalls.</summary>
    Task<IReadOnlyList<PayorPayableItemDto>> GetPayableItemsAsync(Guid payorUserId, CancellationToken ct = default);
}
