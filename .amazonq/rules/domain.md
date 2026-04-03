# EEMO Cantilan — Domain Reference
# Agent: internalize these facts. Use constants exactly as named. Never hardcode values.

---

## Base Classes

### BaseEntity
- `Id: Guid` — initialized to `Guid.NewGuid()`, protected setter

### AuditableEntity : BaseEntity
- Audit fields: `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`
- Soft delete fields: `IsDeleted`, `DeletedAt`, `DeletedBy` — all with protected setters
- Method: `SoftDelete(string deletedBy)` — sets all three fields

---

## Domain Constants

### FeeRates — `Domain/Constants/FeeRates.cs`
Two types of constants — never confuse them:
- **FIXED**: use the constant directly in billing logic
- **RANGE** (Min/Max): used for validation only — actual billing rate always comes from `Stall.MonthlyRate`

| Facility | Constant Name | Value | Type |
|---|---|---|---|
| NPM | `NpmDailyFee` | ₱30.00/day | FIXED |
| NPM | `NpmMonthlyFee` | ₱900.00 | FIXED (reference) |
| NPM | `NpmFishFeePerKilo` | ₱1.00/kg | FIXED |
| TCC | `TccMonthlyMin` | ₱2,400.00 | RANGE (validation only) |
| TCC | `TccMonthlyMax` | ₱4,800.00 | RANGE (validation only) |
| NCC | `NccExtensionMonthly` | ₱1,200.00 | FIXED (Extension area only) |
| NCC | `NccCornerMonthlyMin` | ₱3,240.00 | RANGE (validation only) |
| NCC | `NccCornerMonthlyMax` | ₱3,840.00 | RANGE (validation only) |
| BBQ | `BbqMonthlyMin` | ₱1,600.00 | RANGE (validation only) |
| BBQ | `BbqMonthlyMax` | ₱9,600.00 | RANGE (validation only) |
| ICE | `IceMonthlyMin` | ₱1,000.00 | RANGE (validation only) |
| ICE | `IceMonthlyMax` | ₱2,000.00 | RANGE (validation only) |
| SLH | `SlhHogTotalPerHead` | ₱250.00 | FIXED — use this for billing |
| SLH | `SlhHogSlaughterFee` | ₱50.00 | FIXED (component) |
| SLH | `SlhHogAntemortem` | ₱20.00 | FIXED (component) |
| SLH | `SlhHogTableCharge` | ₱30.00 | FIXED (component) |
| SLH | `SlhHogEntranceFee` | ₱150.00 | FIXED (component) |
| SLH | `SlhLargeTotalPerHead` | ₱365.00 | FIXED — use this for billing |
| SLH | `SlhLargeSlaughterFee` | ₱150.00 | FIXED (component) |
| SLH | `SlhLargePermit` | ₱100.00 | FIXED (component) |
| SLH | `SlhLargeAntemortem` | ₱20.00 | FIXED (component) |
| SLH | `SlhLargePostmortem` | ₱25.00 | FIXED (component) |
| SLH | `SlhLargeTableCharge` | ₱30.00 | FIXED (component) |
| SLH | `SlhLargeLivestockFee` | ₱40.00 | FIXED (component) |

### DomainRules — `Domain/Constants/DomainRules.cs`
- `PaymentHistoryMonths = 12` — rolling window for payment status calculation
- `DelinquentThresholdMonths = 3` — 3+ unpaid months = Delinquent
- `ExpiringSoonMonths = 3` — contract expiring within 3 months = "Expiring Soon"
- `MaxFailedLoginAttempts = 5`
- `LockoutMinutes = 15`

---

## Entities

### Users (TPH — single `Users` table)

Discriminator column: `UserType` (`"Admin"` or `"Collector"`)

**BaseUser : AuditableEntity** (abstract)
- Fields: `FullName`, `Username`, `Email`, `PasswordHash`, `IsActive`, `MustChangePassword`
- Login tracking: `FailedAttempts`, `LockedUntil`, `LastLoginAt`
- Refresh token: `RefreshToken`, `RefreshTokenExpiryTime`
- Computed (ignore in EF): `IsLockedOut`
- Methods: `SetRefreshToken()`, `IsRefreshTokenValid()`, `ClearRefreshToken()`

**AdminUser : BaseUser** — `UserType = "Admin"`
- Additional field: `AdminRole` (enum: `SuperAdmin = 1`, `Admin = 2`)

**CollectorUser : BaseUser** — `UserType = "Collector"`
- Additional fields: `EmployeeId`, `ContactNumber`
- Facility access via junction: `CollectorFacilityAssignment` (never `AssignedArea` string)

**CollectorFacilityAssignment** — junction table
- Links `CollectorUser` ↔ `Facility`
- Unique constraint: `(CollectorId, FacilityId)`

---

### Facilities

**Facility**
- `FacilityCode` enum: `NPM=1`, `TCC=2`, `NCC=3`, `BBQ=4`, `ICE=5`, `SLH=6`
- Has many `Stalls`

**Stall : AuditableEntity**
- Belongs to `Facility`
- Fields: `StallNo`, `MonthlyRate`, `DailyRate` (NPM only, nullable), `Section` (NPM only, nullable), `AreaLocation` (NCC only, nullable)
- Computed (ignore in EF): `IsActive`
- Unique constraint: `(FacilityId, StallNo)`
- **No `PayorId`** — tenant is tracked via `Contract`

**Contract : AuditableEntity**
- One stall can have many contracts over time
- Fields: `ActualOccupant`, `NameOnContract` (can differ), `StartDate`, `EndDate`
- Computed (ignore in EF): `ExpiryDate`, `IsExpired`, `IsExpiringSoon`, `WholeYearRental`

---

### Payments

**PaymentRecord : AuditableEntity**
- One per stall per billing month
- Fields: `StallId`, `BillingYear`, `BillingMonth`, `ORNumber`, `Status` (Unpaid/Partial/Paid), `UtilitiesAmount`, `FishKilos`
- Computed (ignore in EF): `TotalBill`, `BalanceDue`, `AmountPaid`, `FishFeeAmount`, `PeriodKey`
- Unique constraint: `(StallId, BillingYear, BillingMonth)`
- OR Number: manually entered, globally unique, shown only when Paid or Partial

**DailyCollection : AuditableEntity** — NPM only
- One per stall per calendar day
- Fields: `StallId`, `CollectionDate`, `ORNumber`, `FishKilos` (Fish Section only)
- `FishFeeAmount = FishKilos × FeeRates.NpmFishFeePerKilo` — computed, ignore in EF
- `TotalCollected` — computed, ignore in EF
- Unique constraint: `(StallId, CollectionDate)`

---

### Slaughterhouse

**SlaughterTransaction : BaseEntity** (no audit fields — immutable once recorded)
- `AnimalType` enum: `Hog`, `Carabao`, `Cow`
- Two factory methods: `CreateHog()` and `CreateLargeAnimal()`
- Passing `AnimalType.Hog` to `CreateLargeAnimal()` throws `ArgumentException`
- `TotalAmount` — computed, ignore in EF
- Billing: use `FeeRates.SlhHogTotalPerHead` or `FeeRates.SlhLargeTotalPerHead` — never sum components at billing time

---

### Audit

**AuditLog : BaseEntity** (not AuditableEntity — immutable, no soft delete)
- Fields: `Actor`, `Action`, `EntityName`, `EntityId`, `OldValuesJson`, `NewValuesJson`, `Timestamp`

---

## Business Rules

### Delinquency (computed from rolling 12-month window)
- 3+ months unpaid → `Delinquent`
- 1–2 months unpaid → `With Arrears`
- 0 months unpaid → `Up to Date`
- Window size: `DomainRules.PaymentHistoryMonths = 12`

### Contract Expiry
- Expiring within 3 months → `IsExpiringSoon = true` (use `DomainRules.ExpiringSoonMonths`)
- Past end date → `IsExpired = true`
- Both are computed — `builder.Ignore()` in EF config

### NPM Daily Collection
- A `DailyCollection` record is created for every calendar day for every active NPM stall
- Fish Section stalls additionally track `FishKilos` → `FishFeeAmount = kilos × ₱1`
- Reporting stats: Days Collected, Days Missed, Total Daily, Fish Total, Total Fee

### Slaughterhouse
- Use `CreateHog()` for hogs, `CreateLargeAnimal()` for Carabao or Cow
- Guard is enforced inside the factory — agent must not replicate the guard externally

---

## Facilities Quick Reference

| Code | Full Name | Fee Type | Rate Rule |
|---|---|---|---|
| NPM | New Public Market | ₱30/day + utilities | 3 sections: Vegetable, Fish, Meat. Fish adds ₱1/kg. Actual rate: `FeeRates.NpmDailyFee` |
| TCC | Tampak Commercial Center | Monthly | ₱2,400–₱4,800 range. Actual billing → `Stall.MonthlyRate` |
| NCC | New Commercial Center | Monthly | ₱1,200 Extension / ₱3,240–₱3,840 Corner. Actual billing → `Stall.MonthlyRate` |
| BBQ | Barbecue Stand | Monthly | ₱1,600–₱9,600 range. No contract, space only. Actual billing → `Stall.MonthlyRate` |
| ICE | Iceplant | Monthly | ₱1,000–₱2,000 range. No contract, space only. Actual billing → `Stall.MonthlyRate` |
| SLH | Slaughterhouse | Per head | Hog ₱250 / Carabao+Cow ₱365. FIXED — always use `FeeRates` total constants |

---

## Payment Flow

```
Mobile Collector → marks stall Paid/Partial in MAUI app
    → syncs to API
    → Admin opens Web dashboard
    → Admin opens payment modal
    → Admin types OR Number from physical receipt book
    → Saves → stored in PaymentRecord.ORNumber or DailyCollection.ORNumber
```