using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IPayorRepository
{
    /// <summary>Looks up a payor by their login identifier (registered contact number).</summary>
    Task<PayorUser?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default);

    /// <summary>
    /// The payor's own row by id. The id comes from the caller's token, so this can only ever load the caller: it exists
    /// because the payor's portal holds no token it can read, and therefore cannot see its own name or number without
    /// asking. Both are the office's record rather than anything the payor typed.
    /// </summary>
    Task<PayorUser?> GetPayorByIdAsync(Guid payorUserId, CancellationToken ct = default);

    /// <summary>Fetches an unused activation code by its value (null if not found).</summary>
    Task<PayorActivationCode?> GetActivationCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// The name the office holds for whoever occupies the stall: the active contract's actual occupant. Used at activation
    /// so the portal greets the payor by the office's own record instead of asking them to type a name that proves nothing.
    /// <paramref name="municipalityId"/> is required and applied explicitly rather than left to the ambient tenant, because
    /// activation is anonymous: no session has pinned an LGU yet, and a name must never be read from another LGU's stall.
    /// Null when the office holds no name (no active contract, or none recorded).
    /// </summary>
    Task<string?> GetOccupantNameAsync(Guid stallId, Guid municipalityId, CancellationToken ct = default);

    /// <summary>True if any activation code already uses this value (for collision-free generation).</summary>
    Task<bool> ActivationCodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// True if a still-redeemable (unused, unexpired) activation code already exists for this contact
    /// number bound to a DIFFERENT stall. Guards against issuing two codes under one number for two
    /// different stalls/occupants, which would otherwise merge unrelated payors on activation.
    /// </summary>
    Task<bool> ActiveCodeExistsForContactOnOtherStallAsync(string contactNumber, Guid stallId, CancellationToken ct = default);

    /// <summary>
    /// Removes every activation code row for the stall so re-issuing REPLACES (not accumulates) —
    /// the table holds a single activation record per stall/payor at a time. Hard delete by design:
    /// activation codes are ephemeral credentials; the durable activation record is the PayorUser +
    /// PayorStallLink (and the change is captured in the audit log).
    /// </summary>
    Task RemoveCodesForStallAsync(Guid stallId, CancellationToken ct = default);

    Task AddActivationCodeAsync(PayorActivationCode code, CancellationToken ct = default);

    /// <summary>True when the stall is already linked to the given payor.</summary>
    Task<bool> LinkExistsAsync(Guid payorUserId, Guid stallId, CancellationToken ct = default);

    Task AddPayorAsync(PayorUser payor, CancellationToken ct = default);

    Task AddStallLinkAsync(PayorStallLink link, CancellationToken ct = default);

    /// <summary>
    /// Removes every payor→stall link for the stall. Called when a stall's occupancy changes hands
    /// (contract transfer/renewal to a different occupant) so the OUTGOING occupant's payor account can
    /// no longer view or pay the incoming occupant's obligations. The incoming occupant re-establishes
    /// access by activating a fresh code. Returns the number of links removed.
    /// </summary>
    Task<int> RemoveStallLinksAsync(Guid stallId, CancellationToken ct = default);

    /// <summary>Outstanding balances for every stall linked to the payor (read-only dashboard).</summary>
    Task<IReadOnlyList<PayorStallBalanceDto>> GetBalancesAsync(Guid payorUserId, CancellationToken ct = default);

    /// <summary>Per-record payable items (unpaid/partial months) for the payor's linked stalls.</summary>
    Task<IReadOnlyList<PayorPayableItemDto>> GetPayableItemsAsync(Guid payorUserId, CancellationToken ct = default);
}
