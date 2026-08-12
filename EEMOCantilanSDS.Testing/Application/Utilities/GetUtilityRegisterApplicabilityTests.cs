using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Utilities
{
    /// <summary>
    /// The end-of-month utility register must state which utilities each stall is actually metered for.
    ///
    /// Regression: every row reported BOTH electricity and water, so a stall registered with water alone was
    /// listed as owing an electricity reading ("E · Unbilled") for a meter it does not have — and the
    /// utility-bill dialog opened from these screens offered both meters for the same reason. Applicability
    /// comes from the stall's own ApplicableFees flags.
    /// </summary>
    public class GetUtilityRegisterApplicabilityTests
    {
        private static StallDto Stall(string stallNo, bool electricity, bool water) => new(
            Guid.NewGuid(),
            stallNo,
            StallStatus.Active,
            ActualOccupant: $"Payor {stallNo}",
            NameOnContract: $"Payor {stallNo}",
            AreaSqm: 4.8,
            ContractDate: DateTime.Today.AddMonths(-2),
            MonthlyRate: 900m,
            DailyRate: 30m,
            ORNumber: null,
            Section: MarketSection.VegetableArea,
            AreaLocation: null,
            AreaNote: null,
            Remarks: null,
            ContractYears: 3,
            CustomSectionName: null,
            HasElectricity: electricity,
            HasWater: water);

        private static GetUtilityRegisterQueryHandler Handler(params StallDto[] stalls)
        {
            var stallRepo = new Mock<IStallRegisterQueries>();
            stallRepo.Setup(r => r.GetStallsByFacilityAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stalls.ToList());

            var utilityRepo = new Mock<IUtilityBillRepository>();
            utilityRepo.Setup(r => r.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UtilityBill>());

            return new GetUtilityRegisterQueryHandler(stallRepo.Object, utilityRepo.Object);
        }

        [Fact]
        public async Task Register_CarriesEachStallsOwnUtilityApplicability()
        {
            var handler = Handler(
                Stall("1", electricity: true, water: true),
                Stall("2", electricity: false, water: true),
                Stall("3", electricity: true, water: false));

            var result = await handler.Handle(new GetUtilityRegisterQuery(2026, 7, null), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var rows = result.Value!.Rows.ToDictionary(r => r.StallNo);

            Assert.True(rows["1"].HasElectricity);
            Assert.True(rows["1"].HasWater);

            Assert.False(rows["2"].HasElectricity);   // water only — no electricity meter to read
            Assert.True(rows["2"].HasWater);

            Assert.True(rows["3"].HasElectricity);
            Assert.False(rows["3"].HasWater);         // electricity only
        }

        [Fact]
        public async Task Register_OmitsAStallWithNoMeteredUtility()
        {
            // A stall billed for neither has nothing to appear on a utility billing sheet.
            var handler = Handler(
                Stall("1", electricity: true, water: false),
                Stall("2", electricity: false, water: false));

            var result = await handler.Handle(new GetUtilityRegisterQuery(2026, 7, null), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var row = Assert.Single(result.Value!.Rows);
            Assert.Equal("1", row.StallNo);
        }
        [Fact]
        public async Task Register_KeepsAUtilityThatIsNoLongerMeteredButWasAlreadyCharged()
        {
            // A utility can be taken off a stall AFTER a bill was raised. The charge is still owed, and the
            // summary totals still count it, so the row must keep reporting that utility — otherwise the
            // balance disappears from the sheet and the report stops reconciling.
            var stall = Stall("1", electricity: false, water: true);

            var bill = UtilityBill.Create(stall.Id, 2026, 7,
                elecPreviousReading: 0m, elecCurrentReading: 34m, elecRatePerKwh: 1m,
                waterPreviousReading: 0m, waterCurrentReading: 0m, waterRatePerCubicMeter: 1m,
                createdBy: "test");

            var stallRepo = new Mock<IStallRegisterQueries>();
            stallRepo.Setup(r => r.GetStallsByFacilityAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<StallDto> { stall });

            var utilityRepo = new Mock<IUtilityBillRepository>();
            utilityRepo.Setup(r => r.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UtilityBill> { bill });

            var result = await new GetUtilityRegisterQueryHandler(stallRepo.Object, utilityRepo.Object)
                .Handle(new GetUtilityRegisterQuery(2026, 7, null), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var row = Assert.Single(result.Value!.Rows);
            Assert.True(row.HasElectricity);   // no longer metered, but ₱34 was charged this period
            Assert.True(row.HasWater);
        }
    }
}
