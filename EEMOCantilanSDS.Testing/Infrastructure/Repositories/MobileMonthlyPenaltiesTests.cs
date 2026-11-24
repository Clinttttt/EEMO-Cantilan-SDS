using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The collector's monthly list carries each account's contract and the penalties still owed on it (as the Statement of Account
/// reads them), so the collector sees the whole balance and collects it on one receipt. Penalties belong to their own account only.
/// </summary>
public class MobileMonthlyPenaltiesTests : RepositoryTestBase
{
    [Fact]
    public async Task MonthlyMobileCollection_CarriesEachAccountsOwnPenaltiesDue()
    {
        var context = NewContext();
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NCC, "Scratch Center", "SC");
        var late = Stall.Create(facility.Id, "2", 1200m, ApplicableFees.BaseRental);
        var onTime = Stall.Create(facility.Id, "3", 1200m, ApplicableFees.BaseRental);
        var lateContract = Contract.Create(late.Id, "Late Payor", "Late Payor", new DateOnly(2026, 1, 1), 1, 1200m);
        var onTimeContract = Contract.Create(onTime.Id, "On-time Payor", "On-time Payor", new DateOnly(2026, 1, 1), 1, 1200m);
        var penalty = PenaltyDefinition.Create(tenant, "LATE", new DateOnly(2026, 1, 1), "Late payment", null,
            GovernedServiceBasis.DirectApprovedAmount, null, null, true, "head");
        var policy = FineAssessmentPolicy.Create(tenant, penalty.Id, FineAccountKind.FacilityContract, FacilityCode.NCC, "Ordinance",
            FinePenaltyDateBasis.AssessmentDate, true, true, actor, DateTime.UtcNow);
        FineAssessment Fine(Guid contract, string name, decimal amount) => FineAssessment.Create(tenant, FineAccountKind.FacilityContract, contract,
            policy, penalty, "Late Payor", "2", "NCC", new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), null, name, amount,
            Guid.NewGuid(), new string('A', 64), actor, DateTime.UtcNow, "Ordinance");
        context.AddRange(facility, late, onTime, lateContract, onTimeContract, penalty, policy,
            Fine(lateContract.Id, "Surcharge", 300m), Fine(lateContract.Id, "Interest", 30m));
        await context.SaveChangesAsync();

        var result = await new StallRepository(context).GetMobileMonthlyCollectionAsync(FacilityCode.NCC, 2026, 11, new DateOnly(2026, 11, 24), CancellationToken.None);

        var lateRow = result.Stalls.Single(s => s.StallId == late.Id);
        var onTimeRow = result.Stalls.Single(s => s.StallId == onTime.Id);
        Assert.Equal((lateContract.Id, 330m), (lateRow.ContractId, lateRow.PenaltiesDue));
        Assert.Equal((onTimeContract.Id, 0m), (onTimeRow.ContractId, onTimeRow.PenaltiesDue));
        // The rent figures are the rent writer's own and are unchanged by penalties.
        Assert.Equal(2400m, result.OutstandingAmount);
    }
}
