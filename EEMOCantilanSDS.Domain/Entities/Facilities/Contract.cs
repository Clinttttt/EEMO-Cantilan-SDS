using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
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

        /// <summary>
        /// How this occupancy is held. Space-only and extension occupancies have no signed contract behind them, so
        /// the official sheets print "No contract" and leave the contract columns blank, and nothing about them ever
        /// falls due for renewal. Rent is assessed exactly as for a contract.
        /// </summary>
        public OccupancyArrangement Arrangement { get; private set; } = OccupancyArrangement.SignedContract;

        /// <summary>True when a signed contract stands behind this occupancy — the only case with real terms.</summary>
        public bool HasSignedContract => Arrangement == OccupancyArrangement.SignedContract;
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

        /// <summary>
        /// Whether this term owes rent for the given calendar month, for a MONTHLY-billed space.
        /// <para>
        /// A term of N years owes exactly N × 12 months' rent, whatever day of the month it began on. The billing
        /// months are the N × 12 calendar months starting with the month of effectivity: a term running 7 June 2023
        /// to 7 June 2026 owes June 2023 through May 2026 — thirty-six months — because the last billing month ends
        /// the day before the anniversary.
        /// </para>
        /// <para>
        /// The obligation used to bill every calendar month the term OVERLAPPED, which counted both part-months whole
        /// and so charged thirty-seven months for a three-year term (and thirteen for a one-year term). Every
        /// monthly-billed account in the office was over-stated by one month's rent.
        /// </para>
        /// <para>Daily-collected spaces do not use this: they are charged per market day, not by the month.</para>
        /// </summary>
        public bool BillsCalendarMonth(int year, int month)
        {
            if (DurationYears <= 0) return false;

            var firstBillingMonth = EffectivityDate.Year * 12 + (EffectivityDate.Month - 1);
            var asked = year * 12 + (month - 1);
            return asked >= firstBillingMonth && asked < firstBillingMonth + DurationYears * 12;
        }

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
            string createdBy = "System",
            OccupancyArrangement arrangement = OccupancyArrangement.SignedContract)
        {
            var signed = arrangement == OccupancyArrangement.SignedContract;

            return new Contract
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                ActualOccupant = actualOccupant,
                // There is no signed contract, so there is no name on one. Keeping a name here would put it in the
                // "Name of Leasee per signed contract" column of a sheet that must read "No contract".
                NameOnContract = signed ? nameOnContract : null,
                EffectivityDate = effectivityDate,
                // An occupancy without a contract has no term: it runs until the office ends it. The open-ended
                // length keeps it out of renewal and expiry work, and the sheets print no term for it.
                DurationYears = signed ? durationYears : DomainRules.OpenEndedTermYears,
                MonthlyRentalRate = monthlyRate,
                ActualMonthlyRental = actualMonthlyRental,
                Remarks = remarks,
                Arrangement = arrangement,
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
