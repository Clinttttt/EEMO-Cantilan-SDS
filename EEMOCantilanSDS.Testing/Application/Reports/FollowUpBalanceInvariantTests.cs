using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The invariant behind every follow-up figure: within one scope, an occupancy contributes its balance ONCE.
/// <para>
/// It can carry more than one status — a stall can be delinquent AND on a lapsed term needing renewal — and it may
/// show under more than one filter. What it must never do is put its money on the page twice. Nora M. Doloriel's
/// stall 20 read ₱33,300 as "Delinquent" and ₱5,400 as "Contract expired" in the same list: ₱38,700 counted against
/// a ₱33,300 account, on a page the office uses to chase money.
/// </para>
/// <para>
/// The opposite case matters as much. Two genuinely SEPARATE occupancies of one stall — a prior term and the term in
/// force — owe different money for different spans, possibly under different names, and must each keep their row.
/// </para>
/// </summary>
public class FollowUpBalanceInvariantTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 5);
    private static readonly Guid Stall20 = Guid.NewGuid();
    private static readonly Guid Stall14 = Guid.NewGuid();

    /// <summary>Nora's shape: let June 2023 for three years, never collected on, term now lapsed, occupant still there.</summary>
    private static ContractAttentionDto LapsedTerm(Guid stallId, FacilityCode code, string stallNo) =>
        new(stallId, code, stallNo, "Nora M. Doloriel", new DateOnly(2023, 6, 7), new DateOnly(2026, 6, 7), IsExpired: true);

    private static DelinquentStallDto Delinquent(Guid stallId, FacilityCode code, string stallNo, decimal balance) =>
        new(code, stallNo, "Nora M. Doloriel", 37, balance, stallId);

    private static FollowUpQueueDto Compose(
        IReadOnlyList<DelinquentStallDto> delinquency,
        IReadOnlyList<ContractAttentionDto> contracts,
        IReadOnlyList<ClosedStallAccountDto>? ended = null,
        IReadOnlyDictionary<string, decimal>? expiredBalances = null) =>
        FollowUpComposer.Compose(
            2026, 8, AsOf,
            delinquency,
            new Dictionary<FacilityCode, FacilityReportsDto>(),
            Array.Empty<OnlinePaymentAwaitingOrDto>(),
            Array.Empty<SlaughterTransactionDto>(),
            Array.Empty<TrmTripDto>(),
            Array.Empty<TpmVendorAttendanceDto>(),
            Array.Empty<UnreceiptedPaymentDto>(),
            contracts,
            Array.Empty<UtilityBill>(),
            expiredBalances,
            ended);

    [Fact]
    public void ALapsedDelinquentOccupancy_ContributesItsBalanceOnce()
    {
        // Both of Nora's stalls: delinquent, and on a term that lapsed on 7 June 2026 with her still trading.
        var dto = Compose(
            new[]
            {
                Delinquent(Stall20, FacilityCode.ICE, "20", 33_300m),
                Delinquent(Stall14, FacilityCode.NPM, "14", 32_430m),
            },
            new[]
            {
                LapsedTerm(Stall20, FacilityCode.ICE, "20"),
                LapsedTerm(Stall14, FacilityCode.NPM, "14"),
            },
            // The register would also state each term's balance, and the contract row used to pick it up from here.
            expiredBalances: new Dictionary<string, decimal>
            {
                ["ICE|20"] = 5_400m,
                ["NPM|14"] = 4_710m,
            });

        // The money is stated exactly once per stall, by the row that owns it.
        Assert.Equal(33_300m, dto.Items.Where(i => i.Identifier == "Stall 20").Sum(i => i.Amount ?? 0m));
        Assert.Equal(32_430m, dto.Items.Where(i => i.Identifier == "Stall 14").Sum(i => i.Amount ?? 0m));

        // The lapsed status is NOT lost: the renewal row is still there for each stall, carrying no amount.
        foreach (var stallNo in new[] { "Stall 20", "Stall 14" })
        {
            var rows = dto.Items.Where(i => i.Identifier == stallNo).ToList();
            Assert.Contains(rows, i => i.ReasonKind == "delinquent" && i.Amount > 0m);
            var renewal = Assert.Single(rows, i => i.ReasonKind == "contract");
            Assert.Null(renewal.Amount);
            Assert.Equal("Contract expired", renewal.Reason);
        }
    }

    [Fact]
    public void TwoSeparateOccupanciesOfOneStall_KeepTheirOwnRowsAndTheirOwnMoney()
    {
        // Stall 23's shape: Vincent renewed onto a new term in July owing ₱840 for it, while the term before it left
        // ₱32,430 unpaid. Different spans, different debts — the register states the older one and nothing else does,
        // so collapsing them would hide it.
        var stall23 = Guid.NewGuid();
        var priorTerm = new ClosedStallAccountDto(
            stall23, InactiveAccountState.Renewed, FacilityCode.NPM, "New Public Market", "23",
            "Vincent E. Doloriel", "Vincent E. Doloriel", new DateOnly(2023, 6, 7), 3, 900m, null,
            new DateOnly(2026, 6, 7), 0m, 32_430m, null);

        var dto = Compose(
            new[] { new DelinquentStallDto(FacilityCode.NPM, "23", "Vincent E. Doloriel", 1, 840m, stall23) },
            Array.Empty<ContractAttentionDto>(),
            ended: new[] { priorTerm });

        var rows = dto.Items.Where(i => i.Identifier == "Stall 23").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, i => i.Amount == 840m);        // the term in force
        Assert.Contains(rows, i => i.Amount == 32_430m);     // the term before it
        Assert.Equal(33_270m, rows.Sum(i => i.Amount ?? 0m));
    }

    [Fact]
    public void ALapsedOccupancyIsNotStatedTwice_WhenTheRegisterAlsoListsIt()
    {
        // A lapsed account is the occupancy still in force, so the register row and the delinquency row are the same
        // debt. Only one of them may carry it.
        var stall7 = Guid.NewGuid();
        var lapsed = new ClosedStallAccountDto(
            stall7, InactiveAccountState.Lapsed, FacilityCode.ICE, "Iceplant", "7",
            "Merlita A. Abuso", "Merlita A. Abuso", new DateOnly(2023, 6, 7), 3, 900m, null,
            new DateOnly(2026, 6, 7), 0m, 33_300m, null);

        var dto = Compose(
            new[] { Delinquent(stall7, FacilityCode.ICE, "7", 33_300m) },
            Array.Empty<ContractAttentionDto>(),
            ended: new[] { lapsed });

        Assert.Equal(33_300m, dto.Items.Where(i => i.Identifier == "Stall 7").Sum(i => i.Amount ?? 0m));
    }

    [Fact]
    public void EachScopeKeepsItsOwnAmount()
    {
        // The same occupancy, asked about over two spans: a rolling twelve months for a period screen, the whole
        // account for the Financial Reports. The composer states whichever figure it is handed — it must not
        // normalise, cap or blend them.
        var stall = Guid.NewGuid();

        var rolling = Compose(
            new[] { new DelinquentStallDto(FacilityCode.ICE, "7", "Merlita A. Abuso", 11, 9_900m, stall) },
            Array.Empty<ContractAttentionDto>());
        var whole = Compose(
            new[] { new DelinquentStallDto(FacilityCode.ICE, "7", "Merlita A. Abuso", 37, 33_300m, stall) },
            Array.Empty<ContractAttentionDto>());

        Assert.Equal(9_900m, rolling.Items.Where(i => i.Identifier == "Stall 7").Sum(i => i.Amount ?? 0m));
        Assert.Equal(33_300m, whole.Items.Where(i => i.Identifier == "Stall 7").Sum(i => i.Amount ?? 0m));
    }
}
