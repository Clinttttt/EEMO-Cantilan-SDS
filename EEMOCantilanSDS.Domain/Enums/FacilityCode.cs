using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Enums
{
    public enum FacilityCode
    {
        NPM = 1,   // New Public Market
        TCC = 2,   // Tampak Commercial Center
        NCC = 3,   // New Commercial Center
        BBQ = 4,   // Barbecue Stand
        ICE = 5,   // Iceplant
        SLH = 6,   // Slaughterhouse
        TRM = 7,   // Transport Terminal
        TPM = 8,   // Tabo-an Public Market

        // Reserved slots for per-LGU CUSTOM facilities (Phase E). A custom facility is a Head-named,
        // MONTHLY-RENTAL facility that reuses the standard stall/contract/payment/delinquency machinery
        // (all keyed by the Facility's Id, not this code) — so it behaves exactly like TCC/NCC/BBQ/ICE.
        // Numbered 101+ to stay clearly apart from the eight canonical codes.
        Custom1 = 101,
        Custom2 = 102,
        Custom3 = 103,
        Custom4 = 104,
        Custom5 = 105,
    }

    // Billing BEHAVIOUR of a facility, decoupled from its FacilityCode identity (Phase 4). The code says
    // WHICH facility it is; the archetype says HOW it bills — so another LGU can map its own facilities to
    // the right billing behaviour as data, without new code per facility.
    public enum BillingArchetype
    {
        DailyStall = 1,     // per-day stall fee (NPM)
        MonthlyRental = 2,  // monthly stall/space rental (TCC/NCC/BBQ/ICE)
        WeeklyMarket = 3,   // per-vendor per market day (TPM)
        PerTrip = 4,        // per-trip fee (TRM)
        PerHead = 5,        // per-head fee (SLH)
        Custom = 99,
    }

    // Identifies a FIXED ordinance fee rate stored per-LGU in the FacilityRate table (Phase 4). Range /
    // negotiated rates (TCC/NCC/BBQ/ICE monthly) are NOT here — those come from Stall.MonthlyRate.
    public enum FeeRateKey
    {
        NpmDailyStall = 1,    // NPM — ₱ per day
        NpmFishPerKilo = 2,   // NPM Fish — ₱ per kilo
        SlhHogPerHead = 3,    // SLH — ₱ per hog head
        SlhLargePerHead = 4,  // SLH — ₱ per large-animal head
        TpmVendorDay = 5,     // TPM — ₱ per vendor per market day
        TrmPerTrip = 6,       // TRM — ₱ per trip
        ElecPerKwh = 7,       // NPM add-on — default ₱ per kWh (metered; 0 = admin enters per bill)
        WaterPerCubicMeter = 8, // NPM add-on — default ₱ per m³ (metered; 0 = admin enters per bill)
        // NPM — ₱ per month, the rent a market space is LET for. The daily fee above is the installment it is
        // collected in; this is what a month owes. 0 (or no row) means the LGU has not stated one, and the month is
        // taken as the daily fee × DomainRules.DailyBilledMonthDays — which is Cantilan's ₱30 × 30 = ₱900. An LGU
        // whose ordinance states a month that is not thirty of its days (say ₱35 a day and ₱1,000 a month) sets it
        // here, and every obligation, balance and roster figure follows.
        NpmMonthlyStall = 9,
    }
    public enum MarketSection
    {
        VegetableArea = 1,
        FishSection = 2,
        MeatSection = 3,
    }

    /// <summary>
    /// How an occupant holds their space. The office's own registers record three kinds, and the official sheets
    /// print the last two as "No contract" with every contract-derived column left blank — a barbecue stand or an
    /// ice-plant space is let without a signed contract at all, and some commercial-centre spaces are occupied on an
    /// extension of a lapsed one. Rent is assessed and collected in every case; only the contract particulars are
    /// absent, and such an occupancy is open-ended, so it never falls due for renewal.
    /// </summary>
    public enum OccupancyArrangement
    {
        /// <summary>A signed lease contract: a named leasee, an effectivity date, a term and an area.</summary>
        SignedContract = 1,

        /// <summary>Space only — the occupant pays rent with no signed contract behind it.</summary>
        SpaceOnly = 2,

        /// <summary>Occupying past a lapsed contract with the office's leave.</summary>
        Extension = 3,
    }
    public enum NccAreaLocation
    {
        Extension = 1,
        Corner = 2,
        Standard = 3,
    }
    public enum StallStatus
    {
        Active = 1,
        Closed = 2,
    }
    // Category of an INACTIVE stall account on the register. Derived, not stored:
    //   Closed     = explicitly frozen by a head/admin (Status == Closed) — reversible via Reopen. Owes nothing on.
    //   Superseded = handed to a DIFFERENT lessee, or terminated on a stated date. The account is finished.
    //   Renewed    = the SAME lessee took a fresh term; this is their earlier one. They are still in the stall, the
    //                balance is still theirs, and the space is not free to offer to anyone else.
    //   Lapsed     = the term ran out, the space was never handed over and the stall is still open — the tenant is
    //                still there. Reversible via Renew, and STILL COLLECTED: it stays in arrears and follow-up.
    public enum InactiveAccountState
    {
        Closed = 1,
        Expired = 2,
        Superseded = 3,
        Lapsed = 4,
        Renewed = 5,
    }
    public enum StallType
    {
        Permanent = 1,
        Transient = 2,
    }
    public enum PaymentStatus
    {
        Unpaid = 1,
        Partial = 2,
        Paid = 3,
    }
    public enum AnimalType
    {
        Hog = 1,   // ₱250/head
        Carabao = 2,   // ₱365/head
        Cow = 3,   // ₱365/head
        Other = 99,   // Custom animal type with custom rate
    }
    [Flags]
    public enum ApplicableFees
    {
        None = 0,
        BaseRental = 1 << 0,
        DailyRental = 1 << 1,
        Electricity = 1 << 2,
        Water = 1 << 3,
        FishFee = 1 << 4,
    }
    public enum ReportPeriod
    {
        Weekly = 1,
        Monthly = 2,
        Yearly = 3,
    }
    // Reason an admin excused a monthly-rental stall for a billing month (TCC/NCC/BBQ/ICE).
    public enum MonthlyExceptionReason
    {
        VendorNotOperating = 1,
        TemporaryClosure = 2,
        ApprovedByEemo = 3,
        Other = 99,
    }
    // Reason the whole NPM market was closed for a day (excuses every NPM payor that date).
    public enum MarketClosureReason
    {
        Holiday = 1,
        MaintenanceOrFumigation = 2,
        Weather = 3,
        ApprovedByEemo = 4,
        Other = 99,
    }
}
