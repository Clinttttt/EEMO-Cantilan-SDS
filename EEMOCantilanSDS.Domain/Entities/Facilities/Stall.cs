using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Stall : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public Guid FacilityId { get; private set; }
        public string StallNo { get; private set; } = string.Empty;
        public StallStatus Status { get; private set; } = StallStatus.Active;

        // The date the stall was frozen/closed (null when active). Used to excuse the closed span on
        // reopen so a temporary closure never back-bills as arrears.
        public DateOnly? ClosedAt { get; private set; }
        public StallType Type { get; private set; } = StallType.Permanent;
        public ApplicableFees Fees { get; private set; }

        // NPM-specific
        public MarketSection? Section { get; private set; }

        // NPM per-LGU CUSTOM section: when an NPM stall belongs to a section that is NOT one of the three
        // canonical MarketSection values, Section is null and this holds the custom section name (e.g.
        // "Sari-sari Area"). Mirrors how NCC stalls use AreaNote for custom areas. A custom section bills
        // exactly like the Vegetable/Meat sections — flat daily fee, never fish/weight. A stall carries
        // EITHER a canonical Section OR a CustomSectionName, never both.
        public string? CustomSectionName { get; private set; }

        // NCC-specific
        public NccAreaLocation? AreaLocation { get; private set; }

        // Physical info
        public double? AreaSqm { get; private set; }
        public string? AreaNote { get; private set; }
        public string? Remarks { get; private set; }

        // Rates
        public decimal MonthlyRate { get; private set; }
        public decimal? DailyRate { get; private set; }

        public Facility? Facility { get; private set; }
        public ICollection<Contract> Contracts { get; private set; } = new List<Contract>();
        public   ICollection<PaymentRecord> PaymentRecords { get; private set; } = new List<PaymentRecord>();
        public ICollection<DailyCollection> DailyCollections { get; private set; } = new List<DailyCollection>();
        
        private Stall() { }

        public static Stall Create(
            Guid facilityId,
            string stallNo,
            decimal monthlyRate,
            ApplicableFees fees,
            MarketSection? section = null,
            NccAreaLocation? areaLocation = null,
            double? areaSqm = null,
            string? areaNote = null,
            decimal? dailyRate = null,
            string? remarks = null,
            StallType type = StallType.Permanent,
            string createdBy = "System",
            Guid municipalityId = default,
            string? customSectionName = null)
        {
            return new Stall
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityId = facilityId,
                StallNo = stallNo,
                MonthlyRate = monthlyRate,
                DailyRate = dailyRate,
                Fees = fees,
                Section = section,
                // A stall is either a canonical Section OR a custom-named section, never both.
                CustomSectionName = section.HasValue || string.IsNullOrWhiteSpace(customSectionName)
                    ? null : customSectionName.Trim(),
                AreaLocation = areaLocation,
                AreaSqm = areaSqm,
                AreaNote = areaNote,
                Remarks = remarks,
                Status = StallStatus.Active,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void UpdateRates(decimal monthlyRate, decimal? dailyRate = null, string updatedBy = "System")
        {
            MonthlyRate = monthlyRate;
            DailyRate = dailyRate;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void UpdateAreaInfo(double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System")
        {
            AreaSqm = areaSqm;
            AreaNote = areaNote;
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        // Adds utility (electricity/water) applicability without touching the other flags — used by the bulk
        // import so a batch's utility choice also applies to RENEWED (reused expired/closed) stalls. Additive
        // by design: it never strips a fee a stall already carries (e.g. BaseRental/FishFee, or a utility set
        // earlier), so re-importing can't silently remove billing.
        public void AddUtilityFees(bool electricity, bool water, string updatedBy = "System")
        {
            if (electricity) Fees |= ApplicableFees.Electricity;
            if (water) Fees |= ApplicableFees.Water;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void UpdateDetails(string actualOccupant, string? nameOnContract, double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System")
        {
            AreaSqm = areaSqm;
            AreaNote = areaNote;
            Remarks = remarks;
            
            var activeContract = Contracts.FirstOrDefault(c => c.IsActive);
            if (activeContract != null)
            {
                activeContract.UpdateOccupant(actualOccupant, nameOnContract, updatedBy);
            }
            
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void Close(DateOnly closedOn, string updatedBy = "System")
        {
            Status = StallStatus.Closed;
            ClosedAt = closedOn;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void Reopen(string updatedBy = "System")
        {
            Status = StallStatus.Active;
            ClosedAt = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public bool IsActive() => Status == StallStatus.Active;

        /// <summary>True when this NPM stall belongs to a per-LGU CUSTOM section (Section null + a custom name).</summary>
        public bool IsCustomSection => Section is null && !string.IsNullOrWhiteSpace(CustomSectionName);

        /// <summary>
        /// The daily stall fee to bill for this stall. A CUSTOM-section stall uses its own <see cref="DailyRate"/>
        /// (set at registration); every canonical NPM stall (and any stall without a positive custom rate) uses
        /// the tenant's ordinance daily rate, which the caller resolves as-of the collection date. This keeps
        /// Cantilan and all canonical sections on the ordinance rate exactly as before — only custom sections
        /// diverge — so billing, settlement, and report obligations stay in sync via a single rule.
        /// </summary>
        public decimal ResolveDailyFee(decimal ordinanceDailyRate)
            => IsCustomSection && DailyRate is { } r && r > 0m ? r : ordinanceDailyRate;

        /// <summary>
        /// True when this is an EXPIRED account: it has an active contract, but the term of every active
        /// contract has already lapsed (none still covers today), so it is no longer a current holder.
        /// A vacant stall (no active contract) or one still within term returns false. This is the single
        /// source of the stall-level "expired" rule — used by the closed-accounts register, the
        /// stall-holder roster, and the remove-inactive-stall guard so they can never diverge.
        /// </summary>
        public bool IsContractExpired()
        {
            var active = Contracts.Where(c => c.IsActive).ToList();
            return active.Count > 0 && active.All(c => c.IsExpired);
        }

        /// <summary>
        /// True when the physical space is free to take a new occupant: no live contract at all, or every
        /// remaining term has lapsed. This is the one rule that decides whether a vacated stall may be handed to a
        /// new lessee, so the register, the roster and the add-vendor path can never disagree about what "vacant"
        /// means. A closed stall whose lessee's term is still running is NOT vacant — closure is a freeze, not an
        /// end of occupancy. Requires <c>Contracts</c> to be loaded.
        /// </summary>
        public bool IsVacant(DateOnly asOf)
        {
            var live = Contracts.Where(c => c.IsActive).ToList();

            // Nobody holds the space: no term at all, or every remaining term has lapsed. Closure alone does not
            // free it — closing a stall is a temporary freeze (the frozen span is excused when it reopens) and the
            // sitting lessee's term survives it, so handing the space away would end an occupancy the office only
            // meant to pause.
            return live.Count == 0 || live.All(c => c.ExpiryDate < asOf);
        }

        /// <summary>
        /// This stall's occupancy timeline: who held the space, and between which dates. One physical stall may be
        /// let many times over its life, so every historical view — the inactive-account register, a past month's
        /// compliance, a year's report — must read the occupancy that covers the period it is reporting, not
        /// merely the current one. Deriving the windows in one place is what keeps those views from disagreeing.
        ///
        /// <para>An occupancy runs from its effectivity to the EARLIEST of: the day it was terminated, the day
        /// before the next occupancy began, the day the stall was closed, and its own term end. The middle two
        /// cover history recorded before terminations were dated.</para>
        ///
        /// <para>Requires <c>Contracts</c> to be loaded. Ordered oldest first.</para>
        /// </summary>
        public IReadOnlyList<StallOccupancy> Occupancies(DateOnly asOf)
        {
            var ordered = Contracts
                .OrderBy(c => c.EffectivityDate)
                .ThenBy(c => c.CreatedAt)
                .ToList();

            var windows = new List<StallOccupancy>(ordered.Count);

            for (var i = 0; i < ordered.Count; i++)
            {
                var contract = ordered[i];

                // When the occupancy actually ended. A dated termination is the fact of record; otherwise it ran
                // to its term end. Either way it cannot outlast the next lessee's start or the stall's closure.
                var end = contract.EndedOn ?? contract.ExpiryDate;

                if (i + 1 < ordered.Count)
                {
                    var dayBeforeNext = ordered[i + 1].EffectivityDate.AddDays(-1);
                    if (dayBeforeNext < end) end = dayBeforeNext;
                }

                if (Status == StallStatus.Closed && ClosedAt is { } closed && closed < end)
                    end = closed;

                // A window can never end before it starts (bad data, or a same-day handover).
                if (end < contract.EffectivityDate) end = contract.EffectivityDate;

                // Chargeable only within the term: a lessee who stayed on after their term lapsed owes nothing
                // for those days, though any money they did pay in that time is still theirs.
                var billableEnd = end < contract.ExpiryDate ? end : contract.ExpiryDate;
                if (billableEnd < contract.EffectivityDate) billableEnd = contract.EffectivityDate;

                var isCurrent = contract.IsActive && end >= asOf;
                windows.Add(new StallOccupancy(contract, contract.EffectivityDate, end, billableEnd, isCurrent));
            }

            return windows;
        }

        /// <summary>The occupancy that held this stall across (any part of) the given period, newest first.</summary>
        public IReadOnlyList<StallOccupancy> OccupanciesOverlapping(DateOnly periodStart, DateOnly periodEnd, DateOnly asOf) =>
            Occupancies(asOf)
                .Where(o => o.Start <= periodEnd && periodStart <= o.End)
                .ToList();

        /// <summary>
        /// The single occupancy answerable for a monthly billing period — see
        /// <see cref="StallOccupancy.AnsweringForMonth"/>, which is the rule. Null when no occupancy covered it.
        /// </summary>
        public StallOccupancy? OccupancyAnsweringForMonth(int year, int month, DateOnly asOf)
            => StallOccupancy.AnsweringForMonth(Occupancies(asOf), year, month);

        /// <summary>
        /// Which occupancy a collection screen is working on: the term it names, or — when it names none — the most
        /// recent one, which is the sitting lessee on an occupied stall and the lessee whose term lapsed on a stall
        /// nobody has taken since. That fallback is what every collection screen has always meant by "this stall".
        ///
        /// <para>Naming the term is what allows a departed lessee's arrears to be collected without touching the
        /// account of whoever holds the stall now.</para>
        /// </summary>
        public StallOccupancy? ResolveOccupancy(Guid? contractId, DateOnly asOf)
        {
            var windows = Occupancies(asOf);

            if (contractId is { } id && id != Guid.Empty)
                return windows.FirstOrDefault(o => o.Contract.Id == id);

            return windows.Count > 0 ? windows[^1] : null;
        }

        public void SetType(StallType type, string updatedBy = "System")
        {
            Type = type;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Sets this NPM stall's section: EITHER a canonical <see cref="MarketSection"/> OR a per-LGU custom
        /// section name, never both. A non-null <paramref name="section"/> clears any custom name; a custom
        /// name (with null section) clears <see cref="Section"/>. A custom section bills like Vegetable/Meat
        /// — flat daily fee, no fish.
        /// </summary>
        public void SetSection(MarketSection? section, string? customSectionName, string updatedBy = "System")
        {
            Section = section;
            CustomSectionName = section.HasValue || string.IsNullOrWhiteSpace(customSectionName)
                ? null : customSectionName.Trim();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }

}