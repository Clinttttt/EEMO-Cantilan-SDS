# EEMO Admin — Entity Models Documentation

**Project:** EEMOCantilanSDS  
**Modules:** Collectors Management System & Vendors/Stalls Management System  
**Target Framework:** .NET 9  
**Date Created:** $(Date)

---

## Table of Contents

1. [Core Entities - Collectors Module](#core-entities-collectors)
2. [Core Entities - Vendors Module](#core-entities-vendors)
3. [DTOs (Data Transfer Objects)](#dtos)
4. [Enums](#enums)
5. [Relationships](#relationships)
6. [Database Schema Notes](#database-schema-notes)

---

## Core Entities - Collectors

### 1. CollectorAccount

**Purpose:** Represents a revenue collector staff member in the EEMO system.

**Namespace:** `EEMOCantilanSDS.Shared.Models` or `EEMOCantilanSDS.Server.Entities`

```csharp
public class CollectorAccount
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Full name of the collector (3-100 chars)
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Unique employee identifier (e.g., EEMO-2024-001)
    /// </summary>
    public string EmployeeId { get; set; } = string.Empty;

    /// <summary>
    /// Contact phone number (e.g., +63 9xx xxx xxxx)
    /// </summary>
    public string ContactNumber { get; set; } = string.Empty;

    /// <summary>
    /// Email address
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Login username for mobile/web app
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password (never store plaintext)
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Account active/inactive status
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Collection facilities assigned to this collector
    /// One-to-Many relationship via CollectorFacility junction table
    /// </summary>
    public virtual ICollection<CollectorFacility> AssignedFacilities { get; set; } = new List<CollectorFacility>();

    /// <summary>
    /// Total amount collected in current month
    /// Calculated from related Activity records
    /// </summary>
    public decimal CollectedThisMonth { get; set; } = 0;

    /// <summary>
    /// Number of transactions in current month
    /// Calculated from related Activity records
    /// </summary>
    public int TransactionsThisMonth { get; set; } = 0;

    /// <summary>
    /// Last login/activity timestamp
    /// </summary>
    public DateTime LastActive { get; set; } = DateTime.Now;

    /// <summary>
    /// Audit: Record created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Audit: Record last modified timestamp
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Audit: User who created the record
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Audit: User who last modified the record
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Activity history - all transactions by this collector
    /// One-to-Many relationship
    /// </summary>
    public virtual ICollection<CollectorActivity> Activities { get; set; } = new List<CollectorActivity>();
}
```

---

### 2. CollectorFacility

**Purpose:** Junction table for Many-to-Many relationship between Collectors and Facilities.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class CollectorFacility
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to CollectorAccount
    /// </summary>
    public int CollectorId { get; set; }

    /// <summary>
    /// Navigation property to CollectorAccount
    /// </summary>
    public virtual CollectorAccount Collector { get; set; } = null!;

    /// <summary>
    /// Facility code (e.g., NPM, TCC, NCC, BBQ, ICE, SLH)
    /// </summary>
    public string FacilityCode { get; set; } = string.Empty;

    /// <summary>
    /// Facility name (e.g., "New Public Market")
    /// </summary>
    public string FacilityName { get; set; } = string.Empty;

    /// <summary>
    /// Assignment start date
    /// </summary>
    public DateTime AssignedFrom { get; set; } = DateTime.Now;

    /// <summary>
    /// Assignment end date (null = still assigned)
    /// </summary>
    public DateTime? AssignedUntil { get; set; }

    /// <summary>
    /// Unique constraint on (CollectorId, FacilityCode)
    /// </summary>
}
```

---

### 3. CollectorActivity

**Purpose:** Records each payment/transaction collected by a collector.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class CollectorActivity
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to CollectorAccount
    /// </summary>
    public int CollectorId { get; set; }

    /// <summary>
    /// Navigation property to CollectorAccount
    /// </summary>
    public virtual CollectorAccount Collector { get; set; } = null!;

    /// <summary>
    /// Official Receipt (OR) number
    /// Unique constraint recommended
    /// </summary>
    public string OrNumber { get; set; } = string.Empty;

    /// <summary>
    /// Name of person who paid
    /// </summary>
    public string PayorName { get; set; } = string.Empty;

    /// <summary>
    /// Facility code where payment was collected
    /// </summary>
    public string FacilityCode { get; set; } = string.Empty;

    /// <summary>
    /// Nature of payment (e.g., "Stall Rental", "Fish Fee", "Utility Payment")
    /// </summary>
    public string Nature { get; set; } = string.Empty;

    /// <summary>
    /// Amount collected (in Philippine Pesos)
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Payment status (Paid/Unpaid/Partial)
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    /// <summary>
    /// Timestamp of transaction
    /// </summary>
    public DateTime TransactionAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Notes or remarks about the transaction
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Audit: Record created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Soft delete support
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Soft delete timestamp
    /// </summary>
    public DateTime? DeletedAt { get; set; }
}
```

---

### 4. Facility

**Purpose:** Reference entity for available facilities in the system.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class Facility
{
    /// <summary>
    /// Primary key - facility code (e.g., NPM, TCC)
    /// </summary>
    [Key]
    [StringLength(10)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Full facility name
    /// </summary>
    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short description of facility
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Facility address/location
    /// </summary>
    [StringLength(200)]
    public string? Location { get; set; }

    /// <summary>
    /// Whether facility is currently operational
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Contact person for the facility
    /// </summary>
    [StringLength(100)]
    public string? ContactPerson { get; set; }

    /// <summary>
    /// Contact phone number
    /// </summary>
    [StringLength(20)]
    public string? ContactNumber { get; set; }

    /// <summary>
    /// Audit fields
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
```

---

## Core Entities - Vendors

### 1. VendorEntry

**Purpose:** Represents a stall occupant/vendor in a market facility.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class VendorEntry
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Facility code where stall is located (NPM, TCC, NCC, BBQ, ICE, SLH)
    /// Foreign key to Facility
    /// </summary>
    public string FacilityCode { get; set; } = string.Empty;

    /// <summary>
    /// Stall/space number (e.g., 01, F01, M02)
    /// </summary>
    public string StallNo { get; set; } = string.Empty;

    /// <summary>
    /// Actual occupant name (person currently occupying the stall)
    /// </summary>
    public string ActualOccupant { get; set; } = string.Empty;

    /// <summary>
    /// Name on the signed contract (may differ from actual occupant)
    /// </summary>
    public string ContractName { get; set; } = string.Empty;

    /// <summary>
    /// Market section/area name (e.g., "Vegetable Area", "Fish Section", "Meat Section")
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Official Receipt number for contract
    /// </summary>
    public string? OrNo { get; set; }

    /// <summary>
    /// Stall area in square meters
    /// </summary>
    public double AreaSqm { get; set; }

    /// <summary>
    /// Contract start/sign date
    /// </summary>
    public DateTime? ContractDate { get; set; }

    /// <summary>
    /// Number of years contract is valid
    /// </summary>
    public int ContractYears { get; set; }

    /// <summary>
    /// Fixed monthly rental rate (in Philippine Pesos)
    /// </summary>
    public decimal MonthlyRate { get; set; }

    /// <summary>
    /// Actual monthly rental charged (may vary from MonthlyRate)
    /// </summary>
    public decimal ActualMonthlyRental { get; set; }

    /// <summary>
    /// Additional location or area notes
    /// </summary>
    public string AreaLocation { get; set; } = string.Empty;

    /// <summary>
    /// List of fee types applicable to this stall
    /// (e.g., "Electricity", "Water", "Fish ₱1/kg", "Slaughter Fee")
    /// </summary>
    public List<string> FeeTypes { get; set; } = new();

    /// <summary>
    /// Whether stall is currently active/occupied
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether rental was paid this month
    /// </summary>
    public bool IsPaidThisMonth { get; set; }

    /// <summary>
    /// Whether partial rental payment was received this month
    /// </summary>
    public bool IsPartialThisMonth { get; set; }

    /// <summary>
    /// Partial payment amount if applicable
    /// </summary>
    public decimal PartialAmount { get; set; } = 0m;

    /// <summary>
    /// Payment history by month (yyyy-MM format)
    /// Key: month string, Value: paid status
    /// </summary>
    public Dictionary<string, bool> PaymentHistory { get; set; } = new();

    /// <summary>
    /// Daily collection tracking (for applicable sections)
    /// Key: yyyy-MM-dd format, Value: collected status
    /// </summary>
    public Dictionary<string, bool> DailyCollections { get; set; } = new();

    /// <summary>
    /// Daily fish kilo tracking (Fish Section only)
    /// Key: yyyy-MM-dd format, Value: kilos collected that day
    /// </summary>
    public Dictionary<string, decimal> DailyFishKilos { get; set; } = new();

    /// <summary>
    /// Months electricity fee was paid (yyyy-MM format)
    /// </summary>
    public HashSet<string> ElectricityPaidMonths { get; set; } = new();

    /// <summary>
    /// Months water fee was paid (yyyy-MM format)
    /// </summary>
    public HashSet<string> WaterPaidMonths { get; set; } = new();

    /// <summary>
    /// Audit: Record created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Audit: Record last modified timestamp
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Audit: User who created the record
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Audit: User who last modified the record
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Calculated whole year rental (MonthlyRate * 12)
    /// </summary>
    public decimal WholeYearRental => MonthlyRate * 12;

    /// <summary>
    /// Calculated whole year actual rental (ActualMonthlyRental * 12)
    /// </summary>
    public decimal WholeYearActualRental => ActualMonthlyRental > 0 ? ActualMonthlyRental * 12 : WholeYearRental;

    /// <summary>
    /// Navigation property to payment records
    /// One-to-Many relationship
    /// </summary>
    public virtual ICollection<VendorPaymentRecord> PaymentRecords { get; set; } = new List<VendorPaymentRecord>();

    /// <summary>
    /// Navigation property to daily collection records
    /// One-to-Many relationship
    /// </summary>
    public virtual ICollection<DailyCollectionRecord> DailyCollectionRecords { get; set; } = new List<DailyCollectionRecord>();
}
```

---

### 2. VendorPaymentRecord

**Purpose:** Records individual rental and fee payments for vendors.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class VendorPaymentRecord
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to VendorEntry
    /// </summary>
    public int VendorId { get; set; }

    /// <summary>
    /// Navigation property to VendorEntry
    /// </summary>
    public virtual VendorEntry Vendor { get; set; } = null!;

    /// <summary>
    /// Official Receipt number
    /// </summary>
    public string OrNumber { get; set; } = string.Empty;

    /// <summary>
    /// Type of payment (Rental, Electricity, Water, Fish Fee, etc.)
    /// </summary>
    public string PaymentType { get; set; } = string.Empty;

    /// <summary>
    /// Payment month (yyyy-MM format)
    /// </summary>
    public string PaymentMonth { get; set; } = string.Empty;

    /// <summary>
    /// Amount paid (in Philippine Pesos)
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Payment status
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    /// <summary>
    /// Date payment was received
    /// </summary>
    public DateTime PaymentDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Collector who received the payment
    /// </summary>
    public string? CollectedBy { get; set; }

    /// <summary>
    /// Notes or remarks
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Audit: Record created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Soft delete support
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Soft delete timestamp
    /// </summary>
    public DateTime? DeletedAt { get; set; }
}
```

---

### 3. DailyCollectionRecord

**Purpose:** Records daily collection tracking for vendors (primarily for Fish, Vegetable, Meat sections).

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class DailyCollectionRecord
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to VendorEntry
    /// </summary>
    public int VendorId { get; set; }

    /// <summary>
    /// Navigation property to VendorEntry
    /// </summary>
    public virtual VendorEntry Vendor { get; set; } = null!;

    /// <summary>
    /// Collection date
    /// </summary>
    public DateTime CollectionDate { get; set; }

    /// <summary>
    /// Whether collection was made on this date (₱30/day)
    /// </summary>
    public bool IsCollected { get; set; }

    /// <summary>
    /// Amount collected (standard ₱30 for daily collection)
    /// </summary>
    public decimal CollectionAmount { get; set; } = 30m;

    /// <summary>
    /// Daily fish kilo count (Fish Section only)
    /// </summary>
    public decimal? FishKilos { get; set; }

    /// <summary>
    /// Fish fee amount (₱1/kg) - calculated from FishKilos
    /// </summary>
    public decimal? FishFee { get; set; }

    /// <summary>
    /// Collector who recorded this collection
    /// </summary>
    public string? CollectedBy { get; set; }

    /// <summary>
    /// Notes or remarks
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Audit: Record created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Audit: Record last modified timestamp
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
```

---

### 4. VendorFeeStructure

**Purpose:** Defines the fee structure applicable to different vendor sections.

**Namespace:** `EEMOCantilanSDS.Server.Entities`

```csharp
public class VendorFeeStructure
{
    /// <summary>
    /// Primary key identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Section/Type (Vegetable, Fish, Meat, etc.)
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Fee type name (e.g., "Daily Collection", "Fish Fee", "Electricity")
    /// </summary>
    public string FeeTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Base rate (amount per unit)
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// Rate unit (per day, per month, per kg, etc.)
    /// </summary>
    public string RateUnit { get; set; } = string.Empty;

    /// <summary>
    /// Description of the fee
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this fee is active/applicable
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Audit fields
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
```



### CollectorAccountCreateDto

**Purpose:** Request DTO for creating a new collector account.

```csharp
public class CollectorAccountCreateDto
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string EmployeeId { get; set; } = string.Empty;

    [StringLength(20)]
    public string ContactNumber { get; set; } = string.Empty;

    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public List<string> AssignedFacilities { get; set; } = new();
}
```

---

### CollectorAccountUpdateDto

**Purpose:** Request DTO for updating an existing collector account.

```csharp
public class CollectorAccountUpdateDto
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20)]
    public string ContactNumber { get; set; } = string.Empty;

    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Leave empty to keep current password
    /// </summary>
    [StringLength(100, MinimumLength = 8)]
    public string? NewPassword { get; set; }

    [Required]
    public List<string> AssignedFacilities { get; set; } = new();
}
```

---

### CollectorAccountResponseDto

**Purpose:** Response DTO for returning collector data to client.

```csharp
public class CollectorAccountResponseDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> AssignedFacilities { get; set; } = new();
    public decimal CollectedThisMonth { get; set; }
    public int TransactionsThisMonth { get; set; }
    public DateTime LastActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

### CollectorActivityCreateDto

**Purpose:** Request DTO for recording a new transaction/activity.

```csharp
public class CollectorActivityCreateDto
{
    public string OrNumber { get; set; } = string.Empty;
    public string PayorName { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime? TransactionAt { get; set; }
    public string? Remarks { get; set; }
}
```

---

### CollectorActivityResponseDto

**Purpose:** Response DTO for activity/transaction data.

```csharp
public class CollectorActivityResponseDto
{
    public int Id { get; set; }
    public string OrNumber { get; set; } = string.Empty;
    public string PayorName { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime TransactionAt { get; set; }
    public string? Remarks { get; set; }
}
```

---

## Vendor DTOs

### VendorEntryCreateDto

**Purpose:** Request DTO for creating a new vendor entry.

```csharp
public class VendorEntryCreateDto
{
    public string FacilityCode { get; set; } = string.Empty;
    public string StallNo { get; set; } = string.Empty;
    public string ActualOccupant { get; set; } = string.Empty;
    public string ContractName { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string? OrNo { get; set; }
    public double AreaSqm { get; set; }
    public DateTime? ContractDate { get; set; }
    public int ContractYears { get; set; }
    public decimal MonthlyRate { get; set; }
    public decimal ActualMonthlyRental { get; set; }
    public string AreaLocation { get; set; } = string.Empty;
    public List<string> FeeTypes { get; set; } = new();
}
```

---

### VendorEntryUpdateDto

**Purpose:** Request DTO for updating an existing vendor entry.

```csharp
public class VendorEntryUpdateDto
{
    public string ActualOccupant { get; set; } = string.Empty;
    public string ContractName { get; set; } = string.Empty;
    public double AreaSqm { get; set; }
    public DateTime? ContractDate { get; set; }
    public int ContractYears { get; set; }
    public decimal MonthlyRate { get; set; }
    public decimal ActualMonthlyRental { get; set; }
    public string AreaLocation { get; set; } = string.Empty;
    public List<string> FeeTypes { get; set; } = new();
}
```

---

### VendorEntryResponseDto

**Purpose:** Response DTO for returning vendor data to client.

```csharp
public class VendorEntryResponseDto
{
    public int Id { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string StallNo { get; set; } = string.Empty;
    public string ActualOccupant { get; set; } = string.Empty;
    public string ContractName { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string? OrNo { get; set; }
    public double AreaSqm { get; set; }
    public DateTime? ContractDate { get; set; }
    public int ContractYears { get; set; }
    public decimal MonthlyRate { get; set; }
    public decimal ActualMonthlyRental { get; set; }
    public string AreaLocation { get; set; } = string.Empty;
    public List<string> FeeTypes { get; set; } = new();
    public bool IsActive { get; set; }
    public bool IsPaidThisMonth { get; set; }
    public bool IsPartialThisMonth { get; set; }
    public decimal PartialAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

### VendorPaymentRecordCreateDto

**Purpose:** Request DTO for recording a vendor payment.

```csharp
public class VendorPaymentRecordCreateDto
{
    public int VendorId { get; set; }
    public string OrNumber { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string PaymentMonth { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? CollectedBy { get; set; }
    public string? Remarks { get; set; }
}
```

---

### DailyCollectionRecordCreateDto

**Purpose:** Request DTO for recording daily collection.

```csharp
public class DailyCollectionRecordCreateDto
{
    public int VendorId { get; set; }
    public DateTime CollectionDate { get; set; }
    public bool IsCollected { get; set; }
    public decimal CollectionAmount { get; set; } = 30m;
    public decimal? FishKilos { get; set; }
    public string? CollectedBy { get; set; }
    public string? Remarks { get; set; }
}
```

---

## Enums

### PaymentStatus

**Purpose:** Enumeration for payment/transaction status values.

```csharp
public enum PaymentStatus
{
    /// <summary>
    /// Payment received and verified
    /// </summary>
    Paid = 1,

    /// <summary>
    /// Payment pending or not yet received
    /// </summary>
    Unpaid = 2,

    /// <summary>
    /// Partial payment received
    /// </summary>
    Partial = 3,

    /// <summary>
    /// Payment cancelled or voided
    /// </summary>
    Cancelled = 4
}
```

---

### FacilityCode

**Purpose:** Enumeration for facility codes (optional, for type safety).

```csharp
public enum FacilityCodeEnum
{
    /// <summary>New Public Market</summary>
    NPM,

    /// <summary>Tampak Commercial Center</summary>
    TCC,

    /// <summary>New Commercial Center</summary>
    NCC,

    /// <summary>Barbecue Stand</summary>
    BBQ,

    /// <summary>Iceplant</summary>
    ICE,

    /// <summary>Slaughterhouse</summary>
    SLH
}
```

---

## Relationships

### Entity Relationship Diagram (Text)

```
┌─────────────────────┐
│  CollectorAccount   │
├─────────────────────┤
│ Id (PK)             │
│ FullName            │
│ EmployeeId (UNIQUE) │
│ Username (UNIQUE)   │
│ PasswordHash        │
│ IsActive            │
│ LastActive          │
│ CreatedAt           │
│ UpdatedAt           │
└─────────────────────┘
         │
         ├─────────────── 1:N ──────────────┐
         │                                   │
         │                    ┌──────────────────────────┐
         │                    │  CollectorActivity       │
         │                    ├──────────────────────────┤
         │                    │ Id (PK)                  │
         │                    │ CollectorId (FK)         │
         │                    │ OrNumber                 │
         │                    │ PayorName                │
         │                    │ FacilityCode             │
         │                    │ Amount                   │
         │                    │ Status (enum)            │
         │                    │ TransactionAt            │
         │                    │ CreatedAt                │
         │                    └──────────────────────────┘
         │
         └─────────────── M:N ──────────────┐
                                             │
                          ┌──────────────────────────────┐
                          │  CollectorFacility (Junction)│
                          ├──────────────────────────────┤
                          │ Id (PK)                      │
                          │ CollectorId (FK)             │
                          │ FacilityCode (FK)            │
                          │ AssignedFrom                 │
                          │ AssignedUntil (nullable)     │
                          └──────────────────────────────┘
                                      │
                                      └── N:1 ──┐
                                                │
                                    ┌───────────────────┐
                                    │  Facility         │
                                    ├───────────────────┤
                                    │ Code (PK)         │
                                    │ Name              │
                                    │ Description       │
                                    │ Location          │
                                    │ IsActive          │
                                    │ ContactPerson     │
                                    │ ContactNumber     │
                                    └───────────────────┘
```

### Relationship Details

| Relationship | Type | Description |
|---|---|---|
| **CollectorAccount → CollectorActivity** | 1:N | One collector has many transactions |
| **CollectorAccount → CollectorFacility** | 1:N | One collector has many facility assignments |
| **CollectorFacility → Facility** | N:1 | Many assignments point to one facility |
| **CollectorAccount ↔ Facility** | M:N | Many collectors work at many facilities (via CollectorFacility) |

---

## Database Schema Notes

### SQL Table Definitions

#### CollectorAccounts Table

```sql
CREATE TABLE [dbo].[CollectorAccounts] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [FullName] NVARCHAR(100) NOT NULL,
    [EmployeeId] NVARCHAR(50) NOT NULL UNIQUE,
    [ContactNumber] NVARCHAR(20),
    [Email] NVARCHAR(100),
    [Username] NVARCHAR(50) NOT NULL UNIQUE,
    [PasswordHash] NVARCHAR(MAX) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CollectedThisMonth] DECIMAL(18,2) DEFAULT 0,
    [TransactionsThisMonth] INT DEFAULT 0,
    [LastActive] DATETIME2 DEFAULT GETUTCDATE(),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2,
    [CreatedBy] NVARCHAR(100),
    [UpdatedBy] NVARCHAR(100)
);

CREATE INDEX [IX_CollectorAccounts_IsActive] ON [dbo].[CollectorAccounts]([IsActive]);
CREATE INDEX [IX_CollectorAccounts_LastActive] ON [dbo].[CollectorAccounts]([LastActive]);
```

#### CollectorActivities Table

```sql
CREATE TABLE [dbo].[CollectorActivities] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [CollectorId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[CollectorAccounts]([Id]),
    [OrNumber] NVARCHAR(20) NOT NULL UNIQUE,
    [PayorName] NVARCHAR(150) NOT NULL,
    [FacilityCode] NVARCHAR(10) NOT NULL,
    [Nature] NVARCHAR(100) NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Status] INT NOT NULL, -- 1=Paid, 2=Unpaid, 3=Partial, 4=Cancelled
    [TransactionAt] DATETIME2 DEFAULT GETUTCDATE(),
    [Remarks] NVARCHAR(500),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [IsDeleted] BIT DEFAULT 0,
    [DeletedAt] DATETIME2
);

CREATE INDEX [IX_CollectorActivities_CollectorId] ON [dbo].[CollectorActivities]([CollectorId]);
CREATE INDEX [IX_CollectorActivities_FacilityCode] ON [dbo].[CollectorActivities]([FacilityCode]);
CREATE INDEX [IX_CollectorActivities_TransactionAt] ON [dbo].[CollectorActivities]([TransactionAt]);
CREATE INDEX [IX_CollectorActivities_IsDeleted] ON [dbo].[CollectorActivities]([IsDeleted]);
```

#### CollectorFacilities Table (Junction)

```sql
CREATE TABLE [dbo].[CollectorFacilities] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [CollectorId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[CollectorAccounts]([Id]) ON DELETE CASCADE,
    [FacilityCode] NVARCHAR(10) NOT NULL FOREIGN KEY REFERENCES [dbo].[Facilities]([Code]),
    [FacilityName] NVARCHAR(150) NOT NULL,
    [AssignedFrom] DATETIME2 DEFAULT GETUTCDATE(),
    [AssignedUntil] DATETIME2
);

CREATE UNIQUE INDEX [IX_CollectorFacilities_Unique] ON [dbo].[CollectorFacilities]([CollectorId], [FacilityCode]);
CREATE INDEX [IX_CollectorFacilities_FacilityCode] ON [dbo].[CollectorFacilities]([FacilityCode]);
```

#### Facilities Table (Reference)

```sql
CREATE TABLE [dbo].[Facilities] (
    [Code] NVARCHAR(10) PRIMARY KEY,
    [Name] NVARCHAR(150) NOT NULL UNIQUE,
    [Description] NVARCHAR(500),
    [Location] NVARCHAR(200),
    [IsActive] BIT NOT NULL DEFAULT 1,
    [ContactPerson] NVARCHAR(100),
    [ContactNumber] NVARCHAR(20),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2
);
```

---

## API Endpoints (Reference)

Based on these entities, here are the recommended API endpoints for backend development:

```
GET    /api/collectors                       - List all collectors (with filtering)
GET    /api/collectors/{id}                  - Get collector details
POST   /api/collectors                       - Create new collector
PUT    /api/collectors/{id}                  - Update collector
PATCH  /api/collectors/{id}/status           - Toggle active/inactive status
DELETE /api/collectors/{id}                  - Soft delete collector

GET    /api/collectors/{id}/activities       - Get collector's transaction history
GET    /api/collectors/{id}/activities?month=2024-01 - Get monthly transactions
POST   /api/collectors/{id}/activities       - Record new transaction
PUT    /api/collectors/activities/{id}       - Update transaction
DELETE /api/collectors/activities/{id}       - Delete transaction (soft delete)

GET    /api/facilities                       - List all facilities
POST   /api/facilities                       - Create facility
PUT    /api/facilities/{code}                - Update facility
```

---

## Implementation Notes

### For Backend Development

1. **Entity Framework Core Configuration:**
   - Use Fluent API for relationship configuration
   - Enable lazy loading or explicit loading for navigation properties
   - Configure indexes for frequently queried fields

2. **Repository Pattern:**
   - Create repositories for `CollectorAccount`, `CollectorActivity`, `CollectorFacility`
   - Implement `IQueryable` for flexible filtering

3. **Service Layer:**
   - Implement service classes for business logic
   - Handle calculations (CollectedThisMonth, TransactionsThisMonth)
   - Implement soft delete logic for activities

4. **Security:**
   - Never store plaintext passwords - use bcrypt or similar
   - Implement role-based access control (admin, collector, viewer)
   - Add audit trails for sensitive operations

5. **Data Validation:**
   - Use FluentValidation for complex rules
   - Implement unique constraints on database level
   - Validate facility assignments

6. **Queries:**
   - Implement monthly aggregation for collected amounts
   - Create efficient queries for transaction history
   - Handle date range filtering

### For Vendor Module Development

1. **Entity Framework Core Configuration:**
   - Configure VendorEntry with navigation properties for payments and daily collections
   - Set up soft delete filtering for archived vendor entries
   - Configure composite unique constraints (FacilityCode, StallNo)

2. **Repository Pattern:**
   - Create repositories for `VendorEntry`, `VendorPaymentRecord`, `DailyCollectionRecord`
   - Implement filtering by facility and stall number
   - Implement date range queries for payment and collection records

3. **Service Layer:**
   - Calculate monthly rental amounts dynamically
   - Implement daily collection tracking aggregation
   - Calculate fish kilo totals for specific sections
   - Implement payment status determination (Paid/Unpaid/Partial)

4. **Business Logic:**
   - Calculate whole year rental if all months paid
   - Determine if all utilities (electricity, water) are paid
   - Track which months have been paid vs. unpaid
   - Validate stall assignments and facility availability

5. **Data Validation:**
   - Ensure facility codes reference existing facilities
   - Validate stall numbers within facility ranges
   - Verify payment amounts match fee structure rates
   - Check for duplicate collection records on same date

6. **Reporting Queries:**
   - Monthly rental summary by section (vegetables, fish, meat)
   - Utility payment status reports
   - Daily collection trends and totals
   - Vendor arrears and pending payments

---

## Vendor Module - Database Schema

#### VendorEntry Table

```sql
CREATE TABLE [dbo].[VendorEntries] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [FacilityCode] NVARCHAR(10) NOT NULL FOREIGN KEY REFERENCES [dbo].[Facilities]([Code]),
    [StallNo] NVARCHAR(20) NOT NULL,
    [ActualOccupant] NVARCHAR(150) NOT NULL,
    [ContractName] NVARCHAR(150),
    [Section] NVARCHAR(50),
    [MonthlyRate] DECIMAL(18,2),
    [ActualMonthlyRental] DECIMAL(18,2),
    [ElectricityPaidMonths] NVARCHAR(MAX),
    [WaterPaidMonths] NVARCHAR(MAX),
    [ContractDate] DATETIME2,
    [ContractYears] INT,
    [OrNo] NVARCHAR(50),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2,
    [CreatedBy] NVARCHAR(100),
    [UpdatedBy] NVARCHAR(100),
    [IsActive] BIT NOT NULL DEFAULT 1,
    [IsDeleted] BIT DEFAULT 0,
    [DeletedAt] DATETIME2
);

CREATE UNIQUE INDEX [IX_VendorEntries_Unique] ON [dbo].[VendorEntries]([FacilityCode], [StallNo]);
CREATE INDEX [IX_VendorEntries_FacilityCode] ON [dbo].[VendorEntries]([FacilityCode]);
CREATE INDEX [IX_VendorEntries_Section] ON [dbo].[VendorEntries]([Section]);
CREATE INDEX [IX_VendorEntries_IsActive] ON [dbo].[VendorEntries]([IsActive]);
CREATE INDEX [IX_VendorEntries_IsDeleted] ON [dbo].[VendorEntries]([IsDeleted]);
```

#### VendorPaymentRecord Table

```sql
CREATE TABLE [dbo].[VendorPaymentRecords] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [VendorId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[VendorEntries]([Id]) ON DELETE CASCADE,
    [OrNumber] NVARCHAR(50) NOT NULL,
    [PaymentType] NVARCHAR(50) NOT NULL, -- 'Rental', 'Electricity', 'Water'
    [MonthYear] NVARCHAR(7) NOT NULL, -- Format: 'YYYY-MM'
    [Amount] DECIMAL(18,2) NOT NULL,
    [PaymentDate] DATETIME2,
    [PaidBy] NVARCHAR(100),
    [Remarks] NVARCHAR(500),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [IsDeleted] BIT DEFAULT 0,
    [DeletedAt] DATETIME2
);

CREATE UNIQUE INDEX [IX_VendorPaymentRecords_OR] ON [dbo].[VendorPaymentRecords]([OrNumber]);
CREATE INDEX [IX_VendorPaymentRecords_VendorId] ON [dbo].[VendorPaymentRecords]([VendorId]);
CREATE INDEX [IX_VendorPaymentRecords_MonthYear] ON [dbo].[VendorPaymentRecords]([MonthYear]);
CREATE INDEX [IX_VendorPaymentRecords_PaymentType] ON [dbo].[VendorPaymentRecords]([PaymentType]);
CREATE INDEX [IX_VendorPaymentRecords_IsDeleted] ON [dbo].[VendorPaymentRecords]([IsDeleted]);
```

#### DailyCollectionRecord Table

```sql
CREATE TABLE [dbo].[DailyCollectionRecords] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [VendorId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[VendorEntries]([Id]) ON DELETE CASCADE,
    [CollectionDate] DATE NOT NULL,
    [IsCollected] BIT NOT NULL DEFAULT 0,
    [CollectionAmount] DECIMAL(18,2) DEFAULT 30,
    [FishKilos] DECIMAL(10,2),
    [CollectedBy] NVARCHAR(100),
    [Remarks] NVARCHAR(500),
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2,
    [IsDeleted] BIT DEFAULT 0,
    [DeletedAt] DATETIME2
);

CREATE UNIQUE INDEX [IX_DailyCollectionRecords_Unique] ON [dbo].[DailyCollectionRecords]([VendorId], [CollectionDate]);
CREATE INDEX [IX_DailyCollectionRecords_CollectionDate] ON [dbo].[DailyCollectionRecords]([CollectionDate]);
CREATE INDEX [IX_DailyCollectionRecords_IsCollected] ON [dbo].[DailyCollectionRecords]([IsCollected]);
CREATE INDEX [IX_DailyCollectionRecords_IsDeleted] ON [dbo].[DailyCollectionRecords]([IsDeleted]);
```

#### VendorFeeStructure Table

```sql
CREATE TABLE [dbo].[VendorFeeStructures] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [Section] NVARCHAR(50) NOT NULL,
    [FeeTypeName] NVARCHAR(100) NOT NULL,
    [Rate] DECIMAL(18,2) NOT NULL,
    [RateUnit] NVARCHAR(50), -- e.g., 'per day', 'per kg', 'per stall'
    [Description] NVARCHAR(500),
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2
);

CREATE UNIQUE INDEX [IX_VendorFeeStructures_Unique] ON [dbo].[VendorFeeStructures]([Section], [FeeTypeName]);
CREATE INDEX [IX_VendorFeeStructures_Section] ON [dbo].[VendorFeeStructures]([Section]);
CREATE INDEX [IX_VendorFeeStructures_IsActive] ON [dbo].[VendorFeeStructures]([IsActive]);
```

---

## Vendor Module - API Endpoints (Reference)

```
GET    /api/vendors                         - List all vendors (with filtering by facility/section)
GET    /api/vendors/{id}                    - Get vendor details including payment history
POST   /api/vendors                         - Create new vendor entry
PUT    /api/vendors/{id}                    - Update vendor information
DELETE /api/vendors/{id}                    - Soft delete vendor

GET    /api/vendors/{id}/payments           - Get vendor payment history
GET    /api/vendors/{id}/payments?month=2024-01 - Get payments for specific month
POST   /api/vendors/{id}/payments           - Record new payment (rental/utility)
PUT    /api/vendors/payments/{id}           - Update payment record
DELETE /api/vendors/payments/{id}           - Delete payment (soft delete)

GET    /api/vendors/{id}/collections        - Get daily collection history
GET    /api/vendors/{id}/collections?date=2024-01-15 - Get collections for specific date
POST   /api/vendors/{id}/collections        - Record daily collection
PUT    /api/vendors/collections/{id}        - Update collection record
DELETE /api/vendors/collections/{id}        - Delete collection (soft delete)

GET    /api/vendors/summary/daily           - Daily collection summary (all vendors)
GET    /api/vendors/summary/monthly         - Monthly rental summary (all vendors)
GET    /api/vendors/summary/section/{section} - Summary by section (vegetables/fish/meat)
GET    /api/vendors/arrears                 - Get vendors with unpaid amounts
GET    /api/vendors/utilities/status        - Get utility payment status

GET    /api/facilities/{code}/vendors       - List vendors at specific facility
GET    /api/fees/structures                 - List fee structures (Vegetables, Fish, Meat)
```

---

## Version History

| Version | Date | Changes |
|---|---|---|
| 1.0 | 2024 | Initial entity documentation based on Collector.razor component |
| 2.0 | January 2025 | Added Vendor/Stalls module with VendorEntry, VendorPaymentRecord, DailyCollectionRecord, VendorFeeStructure entities; includes DTOs, SQL schemas, and API endpoints for both Collector and Vendor modules |

---

**Document Status:** ✅ Complete - Ready for Backend Implementation  
**Last Updated:** January 2025  
**Next Review:** Upon initial API implementation completion

**Modules Documented:**
- ✅ Collectors Management System (CollectorAccount, CollectorActivity, CollectorFacility, Facility)
- ✅ Vendors/Stalls Management System (VendorEntry, VendorPaymentRecord, DailyCollectionRecord, VendorFeeStructure)
