using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public class StallContractStatusTests
{
    private static readonly DateOnly Today = new(2026, 7, 1);

    private static StallDto Stall(StallStatus status, DateTime? contractDate, int years)
        => new(Guid.NewGuid(), "1", status, "Occupant", null, null, contractDate, 900m, null, null, null, null, null, null, years);

    [Fact]
    public void ActiveStall_WithExpiredContract_IsHidden()
    {
        var dto = Stall(StallStatus.Active, new DateTime(2023, 6, 7), 3); // expires 2026-06-07 (past)
        Assert.False(StallContractStatus.IsCurrentVendor(dto, Today));
    }

    [Fact]
    public void ActiveStall_WithCurrentContract_IsShown()
    {
        var dto = Stall(StallStatus.Active, new DateTime(2024, 1, 1), 3); // expires 2027-01-01 (future)
        Assert.True(StallContractStatus.IsCurrentVendor(dto, Today));
    }

    [Fact]
    public void OnTheExpiryDate_IsStillShown()
    {
        // Seeded so the LAST DAY of the term is today. A term now ends the day before its anniversary — the office's ruling
        // of 2026-09-12 — so three years from 2 Jul 2023 runs through 1 Jul 2026, which is Today here. The point of the test
        // is unchanged: on its final day a stall is still one the office is letting.
        var dto = Stall(StallStatus.Active, new DateTime(2023, 7, 2), 3); // expires exactly today
        Assert.True(StallContractStatus.IsCurrentVendor(dto, Today));
    }

    [Fact]
    public void ClosedStall_IsAlwaysShown_EvenIfContractExpired()
    {
        var dto = Stall(StallStatus.Closed, new DateTime(2023, 6, 7), 3);
        Assert.True(StallContractStatus.IsCurrentVendor(dto, Today));
    }

    [Fact]
    public void StallWithoutDatedContract_IsShown()
    {
        var dto = Stall(StallStatus.Active, null, 0);
        Assert.True(StallContractStatus.IsCurrentVendor(dto, Today));
    }
}
