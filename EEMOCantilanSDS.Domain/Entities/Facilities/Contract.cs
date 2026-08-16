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

        /// <summary>
        /// True when the term had already run out on <paramref name="asOf"/>.
        ///
        /// <para>
        /// Takes the date rather than reading a clock. This decides whether a contract still accrues, and the office asks it of
        /// past dates as well as today — a register for June cannot be answered with August's opinion. As a property reading
        /// the static clock it could only ever answer for the machine's today, and no test could state a different one.
        /// </para>
        ///
        /// <para>
        /// Delegates to <see cref="DomainRules.TermHasExpired"/> so there is ONE expiry rule. There were two: this used to be
        /// <c>asOf &gt; ExpiryDate</c>, which agreed with the shared rule on every contract the platform can now create but
        /// disagreed on a term of zero years — calling it expired the day after it began, where the shared rule said a term
        /// that states no years has not run out. That state is now refused at both entry points, and the two answers can no
        /// longer differ because there is only one.
        /// </para>
        /// </summary>
        public bool IsExpiredOn(DateOnly asOf) => DomainRules.TermHasExpired(EffectivityDate, DurationYears, asOf);

        /// <summary>
        /// True when the term has not run out on <paramref name="asOf"/> but does within
        /// <see cref="DomainRules.ExpiringSoonMonths"/> months of it — the window the office follows up for renewal.
        /// </summary>
        public bool IsExpiringSoonOn(DateOnly asOf) =>
            !IsExpiredOn(asOf) && ExpiryDate <= asOf.AddMonths(DomainRules.ExpiringSoonMonths);

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

            // A signed contract of nought years is not a contract, and the office confirmed (2026-08-16) that it is invalid.
            // Every caller already refused it — the create form's validator, the stallholder import, assigning a past occupant,
            // renewal — but the entity did not, so the one state the platform cannot answer for consistently was still
            // constructible. Refused here so it is unrepresentable rather than merely unreached.
            //
            // Zero remains legitimate for an occupancy WITHOUT a signed contract, where it means "no term was stated": such a
            // row is given the open-ended sentinel just below, which is why it is not treated as expired.
            if (signed && durationYears < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(durationYears),
                    "A signed contract must run for at least one year. Record it as a space-only occupancy if there is no " +
                    "signed term.");
            }

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
        /// Updates the contract effectivity date and duration (e.g. when an admin corrects contract terms from the Vendor
        /// Registry edit form).
        ///
        /// <para>
        /// A SIGNED contract must run at least one year: a term of zero years is not a contract, and it is the state the
        /// office confirmed (2026-08-16) to be invalid. It was reachable — the edit form's validator allowed nought while
        /// every other path required one, and this method only refused negatives — and it produced a row the platform could
        /// not agree with itself about: expiry computed from the term said "expired the day after it began", while the shared
        /// rule in <see cref="DomainRules.TermHasExpired"/> said the term had not run out at all.
        /// </para>
        ///
        /// <para>
        /// An UNSIGNED occupancy has no term to state. Whatever number arrives, it keeps the open-ended sentinel, because a
        /// space let without a contract runs until the office ends it — the same decision <see cref="Create"/> makes. Refusing
        /// instead would break correcting a space-only occupancy's effectivity date, which is a legitimate thing to do.
        /// </para>
        /// </summary>
        public void UpdateTerms(DateOnly effectivityDate, int durationYears, string updatedBy)
        {
            if (durationYears < 0)
                throw new ArgumentOutOfRangeException(nameof(durationYears), "Contract duration cannot be negative.");

            var signed = Arrangement == OccupancyArrangement.SignedContract;

            if (signed && durationYears == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationYears),
                    "A signed contract must run for at least one year. Record it as a space-only occupancy if there is no " +
                    "signed term.");
            }

            EffectivityDate = effectivityDate;
            DurationYears = signed ? durationYears : DomainRules.OpenEndedTermYears;
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
        /// Ends this occupancy, keeping it as history. <paramref name="endedOn"/> is the last day the lessee held the stall.
        ///
        /// <para>
        /// Stated by the caller, never defaulted to today. The end date decides which months belong to the outgoing lessee and
        /// which to the incoming one, so it is nearly always the day BEFORE the handover rather than the day the clerk does the
        /// paperwork — every caller already passed one, and the old default was dead.
        /// </para>
        /// </summary>
        public void Terminate(string updatedBy, DateOnly endedOn)
        {
            IsActive = false;
            EndedOn = endedOn;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
