# EEMO Cantilan SDS — Entity Documentation
**Current Domain Model**

---

## Base Classes

### BaseEntity
**Namespace:** `EEMOCantilanSDS.Domain.Common`

```csharp
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}
```

### AuditableEntity : BaseEntity
**Namespace:** `EEMOCantilanSDS.Domain.Common`

```csharp
public abstract class AuditableEntity : BaseEntity
{
    // Audit fields
    public DateTime CreatedAt { get; protected set; }
    public string? CreatedBy { get; protected set; }
    public DateTime? UpdatedAt { get; protected set; }
    public string? UpdatedBy { get; protected set; }
    
    // Soft delete
    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
    public string? DeletedBy { get; protected set; }
    
    public void SoftDelete(string deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
    }
}
```

**Global Query Filter:** All queries automatically filter `IsDeleted == false`

---

## 1. Users (TPH Inheritance)

### BaseUser : AuditableEntity (Abstract)
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Users`  
**Table:** `Users`  
**Discriminator:** `UserType` (string: "Admin" or "Collector")

```csharp
public abstract class BaseUser : AuditableEntity
{
    // Identity
    public string? FullName { get; protected set; }
    public string? Username { get; protected set; }
    public string? Email { get; protected set; }
    public string PasswordHash { get; protected set; } = string.Empty;
    
    // Status
    public bool IsActive { get; protected set; }
    public bool MustChangePassword { get; protected set; }
    
    // Login tracking
    public int FailedAttempts { get; protected set; }
    public DateTime? LockedUntil { get; protected set; }
    public DateTime? LastLoginAt { get; protected set; }
    
    // Refresh token
    public string? RefreshToken { get; protected set; }
    public DateTime? RefreshTokenExpiryTime { get; protected set; }
    
    // Computed (NOT in DB)
    public bool IsLockedOut => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;
    
    // Methods
    public void SetRefreshToken(string token, DateTime expiry);
    public bool IsRefreshTokenValid(string token);
    public void ClearRefreshToken();
}
```

**Unique Constraints:**
- `Username` (unique)
- `Email` (unique)

### AdminUser : BaseUser
**Discriminator Value:** `"Admin"`

```csharp
public class AdminUser : BaseUser
{
    public AdminRole AdminRole { get; private set; }
    
    public static AdminUser Create(
        string fullName,
        string username,
        string email,
        string password,
        AdminRole role,
        bool mustChangePassword = false);
}
```

**AdminRole Enum:**
```csharp
public enum AdminRole
{
    SuperAdmin = 1,
    Admin = 2
}
```

### CollectorUser : BaseUser
**Discriminator Value:** `"Collector"`

```csharp
public class CollectorUser : BaseUser
{
    public string? EmployeeId { get; private set; }
    public string? ContactNumber { get; private set; }
    
    // Navigation
    public ICollection<CollectorFacilityAssignment> FacilityAssignments { get; private set; }
    
    public static CollectorUser Create(
        string fullName,
        string username,
        string email,
        string password,
        string employeeId,
        string? contactNumber = null);
}
```

**Unique Constraint:** `EmployeeId` (unique)

### CollectorFacilityAssignment : BaseEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Users`  
**Table:** `CollectorFacilityAssignments`

```csharp
public class CollectorFacilityAssignment : BaseEntity
{
    public Guid CollectorId { get; private set; }
    public Guid FacilityId { get; private set; }
    
    // Navigation
    public CollectorUser? Collector { get; private set; }
    public Facility? Facility { get; private set; }
    
    public static CollectorFacilityAssignment Create(
        Guid collectorId,
        Guid facilityId);
}
```

**Unique Constraint:** `(CollectorId, FacilityId)`

---

## 2. Facilities

### Facility : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Facilities`  
**Table:** `Facilities`

```csharp
public class Facility : AuditableEntity
{
    public FacilityCode Code { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    
    // Navigation
    public ICollection<Stall> Stalls { get; private set; }
    public ICollection<CollectorFacilityAssignment> CollectorAssignments { get; private set; }
    
    public static Facility Create(
        FacilityCode code,
        string name,
        string shortName,
        string? description = null);
    
    public void Deactivate();
    public void Activate();
}
```

**FacilityCode Enum:**
```csharp
public enum FacilityCode
{
    NPM = 1,  // New Public Market
    TCC = 2,  // Tampak Commercial Center
    NCC = 3,  // New Commercial Center
    BBQ = 4,  // Barbecue Stand
    ICE = 5,  // Iceplant
    SLH = 6   // Slaughterhouse
}
```

### Stall : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Facilities`  
**Table:** `Stalls`

```csharp
public class Stall : AuditableEntity
{
    public Guid FacilityId { get; private set; }
    public string StallNo { get; private set; } = string.Empty;
    public StallStatus Status { get; private set; } = StallStatus.Active;
    public ApplicableFees Fees { get; private set; }
    
    // NPM-specific
    public MarketSection? Section { get; private set; }
    
    // NCC-specific
    public NccAreaLocation? AreaLocation { get; private set; }
    
    // Physical info
    public double? AreaSqm { get; private set; }
    public string? AreaNote { get; private set; }
    public string? Remarks { get; private set; }
    
    // Rates
    public decimal MonthlyRate { get; private set; }
    public decimal? DailyRate { get; private set; }
    
    // Navigation
    public Facility? Facility { get; private set; }
    public ICollection<Contract> Contracts { get; private set; }
    public ICollection<PaymentRecord> PaymentRecords { get; private set; }
    public ICollection<DailyCollection> DailyCollections { get; private set; }
    
    // Computed (NOT in DB)
    public bool IsActive() => Status == StallStatus.Active;
    
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
        string createdBy = "System");
    
    public void UpdateRates(decimal monthlyRate, decimal? dailyRate = null, string updatedBy = "System");
    public void UpdateAreaInfo(double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System");
    public void UpdateDetails(string actualOccupant, string? nameOnContract, double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System");
    public void Close();
    public void Reopen();
}
```

**Unique Constraint:** `(FacilityId, StallNo)`

**StallStatus Enum:**
```csharp
public enum StallStatus
{
    Active = 1,
    Closed = 2
}
```

**ApplicableFees Enum (Flags):**
```csharp
[Flags]
public enum ApplicableFees
{
    BaseRental = 1,
    Electricity = 2,
    Water = 4,
    FishFee = 8
}
```

**MarketSection Enum (NPM only):**
```csharp
public enum MarketSection
{
    VegetableArea = 1,
    FishSection = 2,
    MeatSection = 3
}
```

**NccAreaLocation Enum (NCC only):**
```csharp
public enum NccAreaLocation
{
    Extension = 1,
    Corner = 2
}
```

### Contract : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Facilities`  
**Table:** `Contracts`

```csharp
public class Contract : AuditableEntity
{
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
    
    // Navigation
    public Stall? Stall { get; private set; }
    
    // Computed (NOT in DB)
    public DateOnly ExpiryDate => EffectivityDate.AddYears(DurationYears);
    public decimal WholeYearRental => MonthlyRentalRate * 12;
    public bool IsExpired => DateOnly.FromDateTime(DateTime.UtcNow) > ExpiryDate;
    public bool IsExpiringSoon => !IsExpired && ExpiryDate <= DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    
    public static Contract Create(
        Guid stallId,
        string actualOccupant,
        string? nameOnContract,
        DateOnly effectivityDate,
        int durationYears,
        decimal monthlyRate,
        decimal? actualMonthlyRental = null,
        string? remarks = null,
        string createdBy = "System");
    
    public void UpdateOccupant(string actualOccupant, string? nameOnContract, string updatedBy);
    public void UpdateRemarks(string? remarks, string updatedBy);
    public void Terminate(string updatedBy);
}
```

---

## 3. Payments

### PaymentRecord : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Payments`  
**Table:** `PaymentRecords`

```csharp
public class PaymentRecord : AuditableEntity
{
    public Guid StallId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public int BillingYear { get; private set; }
    public int BillingMonth { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Unpaid;
    public string? ORNumber { get; private set; }
    public DateTime? PaidAt { get; private set; }
    
    // Fee breakdown
    public decimal BaseRentalAmount { get; private set; }
    public decimal PartialAmount { get; private set; }
    
    // Utilities
    public decimal? ElecReading { get; private set; }
    public decimal? ElecAmount { get; private set; }
    public decimal? WaterReading { get; private set; }
    public decimal? WaterAmount { get; private set; }
    
    // Fish fee — NPM Fish Section only (₱1/kg)
    public decimal? FishKilos { get; private set; }
    
    // Remarks
    public string? Remarks { get; private set; }
    
    // Navigation
    public Stall? Stall { get; private set; }
    
    // Computed (NOT in DB)
    public decimal? FishFeeAmount => FishKilos.HasValue ? FishKilos.Value * 1.00m : null;
    public string PeriodKey => $"{BillingYear:0000}-{BillingMonth:00}";
    public decimal TotalBill => BaseRentalAmount + (ElecAmount ?? 0) + (WaterAmount ?? 0) + (FishFeeAmount ?? 0);
    public decimal AmountPaid => Status == PaymentStatus.Paid ? TotalBill : Status == PaymentStatus.Partial ? PartialAmount : 0;
    public decimal BalanceDue => TotalBill - AmountPaid;
    
    public static PaymentRecord Create(
        Guid stallId,
        int billingYear,
        int billingMonth,
        decimal baseRental,
        string createdBy = "System");
    
    public void RecordPayment(
        string orNumber,
        Guid collectorId,
        PaymentStatus status,
        decimal? partialAmount = null,
        decimal? elecReading = null,
        decimal? elecAmount = null,
        decimal? waterReading = null,
        decimal? waterAmount = null,
        decimal? fishKilos = null,
        string? remarks = null,
        string updatedBy = "System");
    
    public void MarkUnpaid(string updatedBy = "System");
    public void UpdateStatus(PaymentStatus status, decimal partialAmount = 0, string? remarks = null, string updatedBy = "System");
}
```

**Unique Constraint:** `(StallId, BillingYear, BillingMonth)`

**PaymentStatus Enum:**
```csharp
public enum PaymentStatus
{
    Unpaid = 1,
    Partial = 2,
    Paid = 3
}
```

**Business Rules:**
- OR Number must be globally unique across `PaymentRecords` and `DailyCollections`
- OR Number only shown when status is `Paid` or `Partial`
- `PaidAt` is set when status changes to `Paid` or `Partial`
- `PaidAt` is cleared when status changes to `Unpaid`

### DailyCollection : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Payments`  
**Table:** `DailyCollections`  
**Facility:** NPM only

```csharp
public class DailyCollection : AuditableEntity
{
    public Guid StallId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public DateOnly CollectionDate { get; private set; }
    public decimal DailyFee { get; private set; } = FeeRates.NpmDailyFee;
    public bool IsPaid { get; private set; }
    public string? ORNumber { get; private set; }
    
    // Fish Section only
    public decimal? FishKilos { get; private set; }
    
    // Navigation
    public Stall? Stall { get; private set; }
    
    // Computed (NOT in DB)
    public decimal? FishFeeAmount => FishKilos.HasValue ? FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0;
    public decimal TotalCollected => IsPaid ? DailyFee + (FishFeeAmount ?? 0) : 0;
    
    public static DailyCollection Create(
        Guid stallId,
        DateOnly collectionDate,
        string createdBy = "System");
    
    public void MarkPaid(
        string orNumber,
        Guid collectorId,
        decimal? fishKilos = null,
        string updatedBy = "System");
    
    public void MarkUnpaid(string updatedBy = "System");
}
```

**Unique Constraint:** `(StallId, CollectionDate)`

**Business Rules:**
- One record per stall per calendar day
- Fish Section stalls track `FishKilos` → `FishFeeAmount = kilos × ₱1`
- OR Number must be globally unique

---

## 4. Transport Terminal

### TrmTransporter : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.TransportTerminal`  
**Table:** `TrmTransporters`  
**Facility:** TRM only

```csharp
public class TrmTransporter : AuditableEntity
{
    public string Name { get; private set; }
    public string Organization { get; private set; }
    public string DefaultRoute { get; private set; }
    public bool IsActive { get; private set; }
    public string? Remarks { get; private set; }

    // Navigation
    public ICollection<TrmTrip> Trips { get; private set; }

    public static TrmTransporter Create(string name, string organization, string defaultRoute, string? remarks = null, string createdBy = "System");
    public void UpdateDetails(string name, string organization, string defaultRoute, string? remarks, string updatedBy);
    public void Deactivate(string updatedBy);
    public void Activate(string updatedBy);
}
```

### TrmTrip : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.TransportTerminal`  
**Table:** `TrmTrips`  
**Facility:** TRM only

```csharp
public class TrmTrip : AuditableEntity
{
    public Guid TransporterId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public int TripNumber { get; private set; }
    public string DriverName { get; private set; }
    public string PlateNumber { get; private set; }
    public string Route { get; private set; }
    public decimal Fee { get; private set; }  // always FeeRates.TrmTripFee = ₱30
    public string? ORNumber { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string? Remarks { get; private set; }

    // Navigation
    public TrmTransporter? Transporter { get; private set; }

    public static TrmTrip Create(
        Guid transporterId,
        int tripNumber,
        string driverName,
        string plateNumber,
        string route,
        string orNumber,
        Guid? collectorId = null,
        string? remarks = null,
        string createdBy = "System");

    public void UpdateORNumber(string orNumber, string updatedBy);
}
```

**Business Rules:**
- Fee is always `FeeRates.TrmTripFee` (₱30) — fixed, never variable
- OR Number is required at the time of recording a trip
- `TripNumber` is a sequential integer per day, assigned by the handler
- OR Number must be globally unique (validated in FluentValidation)
- `PlateNumber` is stored uppercase

---

## 5. Slaughterhouse

### SlaughterTransaction : AuditableEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Slaughterhouse`  
**Table:** `SlaughterTransactions`  
**Facility:** SLH only

```csharp
public class SlaughterTransaction : AuditableEntity
{
    public Guid FacilityId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public AnimalType AnimalType { get; private set; }
    public int NumberOfHeads { get; private set; }
    public decimal RatePerHead { get; private set; }
    public string? ORNumber { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    
    // Fee breakdown (stored for audit transparency)
    public decimal SlaughterFee { get; private set; }
    public decimal? SlaughterPermit { get; private set; }  // Carabao/Cow only
    public decimal AntemortemFee { get; private set; }
    public decimal? PostmortemFee { get; private set; }  // Carabao/Cow only
    public decimal TableCharge { get; private set; }
    public decimal? EntranceFee { get; private set; }  // Hog only
    public decimal? LivestockFee { get; private set; }  // Carabao/Cow only
    
    // Navigation
    public Facility? Facility { get; private set; }
    
    // Computed (NOT in DB)
    public decimal TotalAmount => RatePerHead * NumberOfHeads;
    
    public static SlaughterTransaction CreateHog(
        Guid facilityId,
        Guid collectorId,
        string ownerName,
        int heads,
        string orNumber,
        DateOnly transactionDate,
        string createdBy = "System");
    
    public static SlaughterTransaction CreateLargeAnimal(
        Guid facilityId,
        Guid collectorId,
        string ownerName,
        AnimalType animalType,
        int heads,
        string orNumber,
        DateOnly transactionDate,
        string createdBy = "System");
}
```

**AnimalType Enum:**
```csharp
public enum AnimalType
{
    Hog = 1,
    Carabao = 2,
    Cow = 3
}
```

**Business Rules:**
- Use `CreateHog()` for hogs
- Use `CreateLargeAnimal()` for Carabao or Cow
- Passing `AnimalType.Hog` to `CreateLargeAnimal()` throws `ArgumentException`
- Billing uses `FeeRates.SlhHogTotalPerHead` (₱250) or `FeeRates.SlhLargeTotalPerHead` (₱365)
- Fee breakdown stored for audit, but never sum components at billing time

---

## 6. Audit

### AuditLog : BaseEntity
**Namespace:** `EEMOCantilanSDS.Domain.Entities.Audit`  
**Table:** `AuditLogs`  
**Note:** Does NOT inherit from AuditableEntity (immutable, no soft delete)

```csharp
public class AuditLog : BaseEntity
{
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string EntityName { get; private set; } = string.Empty;
    public Guid? EntityId { get; private set; }
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public DateTime Timestamp { get; private set; }
    
    public static AuditLog Create(
        string actor,
        string action,
        string entityName,
        Guid? entityId = null,
        string? oldValuesJson = null,
        string? newValuesJson = null);
}
```

**Business Rules:**
- Immutable once created
- No soft delete
- No audit fields (CreatedAt, UpdatedAt, etc.)
- Stores JSON snapshots of entity changes

---

## Entity Relationships

```
Facility (1) ──────< (M) Stall
                         │
                         ├──< (M) Contract
                         ├──< (M) PaymentRecord
                         └──< (M) DailyCollection

CollectorUser (M) >────< (M) Facility
                 (via CollectorFacilityAssignment)

AdminUser (no relationships)

Facility (1) ──────< (M) SlaughterTransaction

TrmTransporter (1) ──────< (M) TrmTrip
```

---

## Database Tables Summary

| Table | Entity | Inheritance | Soft Delete | Audit Fields |
|-------|--------|-------------|-------------|--------------|
| Users | BaseUser (TPH) | AdminUser, CollectorUser | ✅ | ✅ |
| CollectorFacilityAssignments | CollectorFacilityAssignment | - | ❌ | ❌ |
| Facilities | Facility | - | ✅ | ✅ |
| Stalls | Stall | - | ✅ | ✅ |
| Contracts | Contract | - | ✅ | ✅ |
| PaymentRecords | PaymentRecord | - | ✅ | ✅ |
| DailyCollections | DailyCollection | - | ✅ | ✅ |
| SlaughterTransactions | SlaughterTransaction | - | ✅ | ✅ |
| TpmVendors | TpmVendor | - | ✅ | ✅ |
| TpmAttendances | TpmAttendance | - | ✅ | ✅ |
| TrmTransporters | TrmTransporter | - | ✅ | ✅ |
| TrmTrips | TrmTrip | - | ✅ | ✅ |
| AuditLogs | AuditLog | - | ❌ | ❌ |

---

## Computed Properties (NOT in Database)

These properties are calculated in C# code and must be ignored in EF configuration:

**BaseUser:**
- `IsLockedOut`

**Stall:**
- `IsActive()` (method, not property)

**Contract:**
- `ExpiryDate`
- `IsExpired`
- `IsExpiringSoon`
- `WholeYearRental`

**PaymentRecord:**
- `FishFeeAmount`
- `PeriodKey`
- `TotalBill`
- `AmountPaid`
- `BalanceDue`

**DailyCollection:**
- `FishFeeAmount`
- `TotalCollected`

**SlaughterTransaction:**
- `TotalAmount`

---

## Unique Constraints

| Table | Columns |
|-------|---------|
| Users | Username |
| Users | Email |
| Users | EmployeeId (CollectorUser only) |
| CollectorFacilityAssignments | (CollectorId, FacilityId) |
| Stalls | (FacilityId, StallNo) |
| PaymentRecords | (StallId, BillingYear, BillingMonth) |
| DailyCollections | (StallId, CollectionDate) |

---

## Global OR Number Uniqueness

OR Numbers must be unique across:
- `PaymentRecords.ORNumber`
- `DailyCollections.ORNumber`
- `TpmAttendances.ORNumber`
- `TrmTrips.ORNumber`

Validated in FluentValidation, not database constraint.
