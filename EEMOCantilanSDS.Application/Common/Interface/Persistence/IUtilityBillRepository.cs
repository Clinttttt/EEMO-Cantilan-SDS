using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IUtilityBillRepository
{
    /// <summary>Tracked lookup by id (for payment mutation).</summary>
    Task<UtilityBill?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked lookup by stall + billing month (for reading create/update).</summary>
    Task<UtilityBill?> GetByStallAndMonthAsync(Guid stallId, int year, int month, CancellationToken ct = default);

    /// <summary>The stall's most recent bill strictly before the given month (for carry-forward readings).</summary>
    Task<UtilityBill?> GetLatestBeforeAsync(Guid stallId, int year, int month, CancellationToken ct = default);

    /// <summary>All bills for a billing month (read-only, for the register).</summary>
    Task<IReadOnlyList<UtilityBill>> GetForMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>A stall's full utility history, newest month first (read-only).</summary>
    Task<IReadOnlyList<UtilityBill>> GetAllForStallAsync(Guid stallId, CancellationToken ct = default);

    Task AddAsync(UtilityBill bill, CancellationToken ct = default);

    /// <summary>True when no other utility bill already uses this OR number (on either utility).
    /// <paramref name="excludeBillId"/> lets the current bill re-use its own OR when re-marking.</summary>
    Task<bool> IsORNumberUniqueAsync(string orNumber, Guid? excludeBillId = null, CancellationToken ct = default);
}
