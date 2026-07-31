using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Contract : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid StallId { get; private set; }
        public string? ORNumber { get; private set; }
        public string ActualOccupant { get; private set; } = string.Empty;
        public string? NameOnContract { get; private set; }
        public DateOnly EffectivityDate { get; private set; }
        public int DurationYears { get; private set; }
        public decimal MonthlyRentalRate { get; private set; }
        public decimal? ActualMonthlyRental { get; private set; }
        public bool IsActive { get; private set; } = true;
        public string? Remarks { get; private set; }
        public Stall? Stall { get; private set; }
        private Contract() { }

        public DateOnly ExpiryDate => ComputeExpiry(EffectivityDate, DurationYears);

        /// <summary>
        /// The single source of the contract-expiry formula: a term runs <paramref name="durationYears"/>
        /// years from <paramref name="effectivityDate"/>. Shared by the entity (<see cref="ExpiryDate"/>)
        /// and the DTO-based facility view so the "expired" rule can never drift between them.
        /// </summary>
        public static DateOnly ComputeExpiry(DateOnly effectivityDate, int durationYears) =>
            effectivityDate.AddYears(durationYears);

        public decimal WholeYearRental => MonthlyRentalRate * 12;
        public bool IsExpired => PhilippineTime.Today > ExpiryDate;
        public bool IsExpiringSoon => !IsExpired &&
                                         ExpiryDate <= PhilippineTime.Today.AddMonths(3);

        /// <summary>
        /// Collection eligibility for a specific business date: the contract must be active AND its term
        /// must cover that date (EffectivityDate ≤ date ≤ ExpiryDate). Use this — never <see cref="IsActive"/>
        /// alone — for collection/report eligibility, because <see cref="IsActive"/> is a manual flag that
        /// does not reflect whether the term has lapsed.
        /// </summary>
        public bool IsCollectableOn(DateOnly date) =>
            IsActive && EffectivityDate <= date && date <= ExpiryDate;

        /// <summary>
        /// Period eligibility for month-level views: the contract must be active AND its term must overlap
        /// the inclusive period [<paramref name="periodStart"/>, <paramref name="periodEnd"/>].
        /// </summary>
        public bool OverlapsPeriod(DateOnly periodStart, DateOnly periodEnd) =>
            IsActive && EffectivityDate <= periodEnd && periodStart <= ExpiryDate;
        public static Contract Create(Guid stallId,string actualOccupant,
            string? nameOnContract,
            DateOnly effectivityDate,
            int durationYears,
            decimal monthlyRate,
            decimal? actualMonthlyRental = null,
            string? remarks = null,
            string createdBy = "System")
        {
            return new Contract
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                ActualOccupant = actualOccupant,
                NameOnContract = nameOnContract,
                EffectivityDate = effectivityDate,
                DurationYears = durationYears,
                MonthlyRentalRate = monthlyRate,
                ActualMonthlyRental = actualMonthlyRental,
                Remarks = remarks,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void UpdateOccupant(string actualOccupant, string? nameOnContract, string updatedBy)
        {
            ActualOccupant = actualOccupant;
            NameOnContract = nameOnContract;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Updates the contract effectivity date and duration (e.g. when an admin corrects
        /// contract terms from the Vendor Registry edit form).
        /// </summary>
        public void UpdateTerms(DateOnly effectivityDate, int durationYears, string updatedBy)
        {
            if (durationYears < 0)
                throw new ArgumentOutOfRangeException(nameof(durationYears), "Contract duration cannot be negative.");

            EffectivityDate = effectivityDate;
            DurationYears = durationYears;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void UpdateRemarks(string? remarks, string updatedBy)
        {
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// The day this occupancy actually ended, when it ended EARLY — the office terminated it before the term
        /// ran out (a handover, a transfer, a closure). Null when it simply ran its course, in which case the term
        /// end (<see cref="ExpiryDate"/>) is the end of the occupancy. Recorded because money and arrears must be
        /// attributed to the lessee who actually held the stall on the day concerned, and without this an early
        /// handover would credit or bill the outgoing lessee for the incoming one's months.
        /// </summary>
        public DateOnly? EndedOn { get; private set; }

        /// <summary>
        /// Ends this occupancy, keeping it as history. <paramref name="endedOn"/> is the last day the lessee held
        /// the stall; when omitted, today is used (an early termination always ends on the day it is done).
        /// </summary>
        public void Terminate(string updatedBy, DateOnly? endedOn = null)
        {
            IsActive = false;
            EndedOn = endedOn ?? PhilippineTime.Today;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
