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
    }
    public enum MarketSection
    {
        VegetableArea = 1,
        FishSection = 2,
        MeatSection = 3,
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
