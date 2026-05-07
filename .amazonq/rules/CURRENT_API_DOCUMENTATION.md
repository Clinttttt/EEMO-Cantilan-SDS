# EEMO Cantilan SDS — API Documentation
**Current Implementation Status**

---

## Base URL
```
https://localhost:7xxx/api
```

---

## Authentication

All endpoints except `/setup/*` and `/admin-auth/login` require JWT Bearer token in Authorization header or cookie-based authentication.

### Cookie-Based Auth
- Access Token: 15 minutes expiry
- Refresh Token: 7 days expiry (httpOnly cookie)

---

## 1. Setup Controller
**Base Route:** `/api/setup`  
**Authorization:** AllowAnonymous

### GET `/setup/status`
Check if system setup is required (SuperAdmin exists).

**Response:** `SetupStatusDto`
```json
{
  "isSetupRequired": true
}
```

### POST `/setup/create-first-admin`
Create the first SuperAdmin account.

**Request Body:** `CreateFirstAdminCommand`
```json
{
  "fullName": "John Doe",
  "username": "admin",
  "email": "admin@cantilan.gov.ph",
  "password": "SecurePass123!"
}
```

**Response:** `bool` (true on success)

**Business Rules:**
- Only works if no SuperAdmin exists
- Returns 409 Conflict if SuperAdmin already exists
- Password must meet security requirements

---

## 2. Admin Auth Controller
**Base Route:** `/api/admin-auth`

### POST `/admin-auth/login`
Admin/SuperAdmin login.

**Request Body:** `LoginCommand`
```json
{
  "username": "admin",
  "password": "SecurePass123!"
}
```

**Response:** `TokenResponseDto`
```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "refresh_token_here",
  "expiresIn": 900
}
```

**Side Effects:**
- Sets auth cookies (access token + refresh token)
- Updates LastLoginAt
- Resets FailedAttempts on success
- Increments FailedAttempts on failure
- Locks account after 5 failed attempts (15 minutes)

### POST `/admin-auth/refresh-token`
Refresh access token using refresh token from cookie.

**Response:** `TokenResponseDto`

**Business Rules:**
- Reads refresh token from httpOnly cookie
- Returns 401 if refresh token invalid/expired
- Issues new access token + refresh token pair

### GET `/admin-auth/current-user`
Get currently authenticated admin user details.

**Authorization:** Required

**Response:** `AdminUserDto`
```json
{
  "id": "guid",
  "fullName": "John Doe",
  "username": "admin",
  "email": "admin@cantilan.gov.ph",
  "role": "SuperAdmin",
  "isActive": true,
  "mustChangePassword": false
}
```

### POST `/admin-auth/logout`
Logout current user.

**Authorization:** Required

**Response:**
```json
{
  "message": "Logged out successfully"
}
```

**Side Effects:**
- Clears auth cookies
- Does NOT invalidate refresh token in DB (stateless logout)

---

## 3. Stalls Controller
**Base Route:** `/api/stalls`  
**Authorization:** Required

### POST `/stalls`
Create a new stall.

**Request Body:** `CreateStallCommand`
```json
{
  "facilityId": "guid",
  "stallNo": "A-01",
  "monthlyRate": 2400.00,
  "fees": 1,
  "section": 1,
  "areaLocation": null,
  "areaSqm": 10.5,
  "areaNote": "Corner slot",
  "dailyRate": null,
  "actualOccupant": "Juan Dela Cruz",
  "nameOnContract": "Juan Dela Cruz",
  "contractDate": "2023-06-07",
  "durationYears": 3
}
```

**Response:** `StallDto`

**Business Rules:**
- StallNo must be unique per facility
- MonthlyRate must be within facility-specific range (validated)
- Creates both Stall and Contract entities

### PUT `/stalls/{stallId}`
Update stall details (full update).

**Request Body:** `UpdateStallCommand`
```json
{
  "stallId": "guid",
  "monthlyRate": 2400.00,
  "fees": 3,
  "areaSqm": 10.5,
  "areaNote": "Corner slot, near entrance",
  "dailyRate": null,
  "actualOccupant": "Juan Dela Cruz",
  "nameOnContract": "Juan Dela Cruz",
  "remarks": "Updated contract terms"
}
```

**Response:** `StallDto`

**Business Rules:**
- Updates stall rates and area info
- Updates active contract occupant details
- Returns 404 if stall not found

### PATCH `/stalls/{stallId}/details`
Update stall details (partial update).

**Request Body:** `UpdateStallDetailsCommand`
```json
{
  "stallId": "guid",
  "actualOccupant": "Juan Dela Cruz",
  "nameOnContract": "Juan Dela Cruz",
  "areaSqm": 10.5,
  "areaNote": "Corner slot"
}
```

**Response:** `bool`

### PATCH `/stalls/{stallId}/status`
Toggle stall status (Active/Closed).

**Request Body:**
```json
{
  "close": true
}
```

**Response:** `bool`

**Business Rules:**
- `close: true` → sets status to Closed
- `close: false` → sets status to Active

### GET `/stalls/facility/{facilityCode}/holders-list`
Get stall holders list for a facility (for dropdown/selection).

**Query Parameters:**
- `facilityCode` (required): NPM, TCC, NCC, BBQ, ICE, SLH
- `section` (optional): MarketSection enum (NPM only)
- `searchTerm` (optional): Filter by occupant name or stall number

**Response:** `StallHoldersListDto`
```json
{
  "facilityCode": "NPM",
  "facilityName": "New Public Market",
  "totalStalls": 120,
  "activeStalls": 115,
  "holders": [
    {
      "stallId": "guid",
      "stallNo": "A-01",
      "actualOccupant": "Juan Dela Cruz",
      "section": "VegetableArea",
      "monthlyRate": 900.00,
      "isPaid": true
    }
  ]
}
```

### GET `/stalls/facility/{facilityCode}/paginated`
Get paginated stalls for a facility (cursor-based pagination).

**Query Parameters:**
- `facilityCode` (required): NPM, TCC, NCC, BBQ, ICE, SLH
- `section` (optional): MarketSection enum (NPM only)
- `cursor` (optional): DateTime cursor for pagination
- `pageSize` (optional, default: 20, max: 100)

**Response:** `CursorPagedResult<StallDto>`
```json
{
  "items": [
    {
      "id": "guid",
      "stallNo": "A-01",
      "status": "Active",
      "actualOccupant": "Juan Dela Cruz",
      "nameOnContract": "Juan Dela Cruz",
      "areaSqm": 10.5,
      "contractDate": "2023-06-07T00:00:00Z",
      "monthlyRate": 900.00,
      "orNumber": "OR-2025-001",
      "section": "VegetableArea",
      "areaLocation": null,
      "areaNote": "Corner slot",
      "remarks": "Good standing tenant"
    }
  ],
  "nextCursor": "2025-01-15T10:30:00Z",
  "hasMore": true
}
```

### GET `/stalls/{stallId}/payment-history`
Get 12-month payment history for a stall.

**Response:** `IReadOnlyList<PaymentHistoryDto>`
```json
[
  {
    "id": "guid",
    "stallId": "guid",
    "period": "2025-01",
    "billingYear": 2025,
    "billingMonth": 1,
    "status": "Paid",
    "totalBill": 900.00,
    "totalPaid": 900.00,
    "balanceDue": 0.00,
    "orNumber": "OR-2025-001",
    "paidAt": "2025-01-05T08:30:00Z"
  }
]
```

---

## 4. Payments Controller
**Base Route:** `/api/payments`  
**Authorization:** Required

### GET `/payments/stall/{stallId}`
Get payment record for a specific stall and billing period.

**Query Parameters:**
- `year` (required): Billing year
- `month` (required): Billing month (1-12)

**Response:** `PaymentRecordDto`
```json
{
  "id": "guid",
  "stallId": "guid",
  "billingYear": 2025,
  "billingMonth": 1,
  "status": "Paid",
  "orNumber": "OR-2025-001",
  "paidAt": "2025-01-05T08:30:00Z",
  "baseRentalAmount": 900.00,
  "elecReading": 150.5,
  "elecAmount": 1500.00,
  "waterReading": 20.0,
  "waterAmount": 200.00,
  "fishKilos": 50.0,
  "totalBill": 2650.00,
  "totalPaid": 2650.00,
  "balanceDue": 0.00,
  "remarks": "Paid on time"
}
```

**Returns:** 404 if no payment record exists for that period

### POST `/payments/record`
Record payment status (Paid/Partial/Unpaid).

**Request Body:** `RecordPaymentCommand`
```json
{
  "stallId": "guid",
  "year": 2025,
  "month": 1,
  "status": "Paid",
  "partialAmount": null,
  "remarks": "Paid in full"
}
```

**Response:** `bool`

**Business Rules:**
- Creates PaymentRecord if doesn't exist
- Updates existing PaymentRecord if exists
- `partialAmount` required if status is Partial
- `partialAmount` must be > 0 and < TotalBill
- Sets PaidAt timestamp when status is Paid or Partial
- Clears PaidAt when status is Unpaid

### POST `/payments/or-number`
Save OR number for a payment record.

**Request Body:** `SaveOrNumberCommand`
```json
{
  "stallId": "guid",
  "year": 2025,
  "month": 1,
  "orNumber": "OR-2025-001",
  "elecReading": 150.5,
  "elecAmount": 1500.00,
  "waterReading": 20.0,
  "waterAmount": 200.00,
  "fishKilos": 50.0,
  "remarks": "Late payment penalty waived"
}
```

**Response:** `bool`

**Business Rules:**
- OR Number must be globally unique across PaymentRecords and DailyCollections
- Only saves OR when status is Paid or Partial
- Updates utility readings and amounts
- Validates OR uniqueness in FluentValidation

---

## 5. Facilities Controller
**Base Route:** `/api/facilities`  
**Authorization:** Required

### GET `/facilities/{facilityCode}/stalls`
Get all stalls for a facility (non-paginated).

**Query Parameters:**
- `facilityCode` (required): NPM, TCC, NCC, BBQ, ICE, SLH
- `section` (optional): MarketSection enum (NPM only)

**Response:** `IReadOnlyList<StallDto>`

**Use Case:** For loading all stalls at once (e.g., facility dashboard)

### GET `/facilities/{facilityCode}/sections`
Get summary statistics per section (NPM only).

**Query Parameters:**
- `facilityCode` (required): Must be NPM
- `year` (required): Billing year
- `month` (required): Billing month

**Response:** `Dictionary<MarketSection, StallSummaryDto>`
```json
{
  "VegetableArea": {
    "totalStalls": 40,
    "activeStalls": 38,
    "paidStalls": 35,
    "partialStalls": 2,
    "unpaidStalls": 1,
    "totalCollected": 31500.00,
    "totalOutstanding": 2700.00
  },
  "FishSection": {
    "totalStalls": 30,
    "activeStalls": 28,
    "paidStalls": 25,
    "partialStalls": 1,
    "unpaidStalls": 2,
    "totalCollected": 22500.00,
    "totalOutstanding": 2700.00
  }
}
```

### GET `/facilities/{facilityCode}/summary`
Get facility-wide summary statistics.

**Query Parameters:**
- `facilityCode` (required): NPM, TCC, NCC, BBQ, ICE, SLH
- `year` (required): Billing year
- `month` (required): Billing month

**Response:** `FacilitySummaryDto`
```json
{
  "facilityCode": "TCC",
  "facilityName": "Tampak Commercial Center",
  "totalStalls": 50,
  "activeStalls": 48,
  "paidStalls": 45,
  "partialStalls": 2,
  "unpaidStalls": 1,
  "totalCollected": 108000.00,
  "totalOutstanding": 7200.00,
  "collectionRate": 93.75
}
```

---

## 6. Vendors Controller
**Base Route:** `/api/vendors`  
**Authorization:** Required

### GET `/vendors/registry`
Get vendor registry with summary statistics and vendor list.

**Query Parameters:**
- `year` (required): Billing year
- `month` (required): Billing month (1-12)

**Response:** `VendorRegistryDto`
```json
{
  "totalVendors": 20,
  "activeVendors": 19,
  "closedVendors": 1,
  "paidThisMonth": 12,
  "unpaidCount": 7,
  "totalOutstanding": 14040.00,
  "monthlyTarget": 31980.00,
  "vendors": [
    {
      "stallId": "guid",
      "stallNo": "01",
      "actualOccupant": "Trugillo, Elpedia C.",
      "nameOnContract": "Nila F. Andoy",
      "orNumber": "OR-2024-001",
      "facilityCode": 1,
      "facilityName": "New Public Market",
      "section": 1,
      "sectionDisplay": "VegetableArea",
      "areaLocation": null,
      "areaLocationDisplay": null,
      "monthlyRate": 900.00,
      "status": 1,
      "paymentStatus": 3
    }
  ]
}
```

**Business Rules:**
- Vendors are derived from Stalls with active Contracts
- Payment status determined by PaymentRecord for specified billing period
- Outstanding balance = TotalBill - AmountPaid for each active stall
- Monthly target = sum of all active stall monthly rates
- Stalls without contracts show "No Contract" as actualOccupant

---

## 8. Slaughterhouse Controller
**Base Route:** `/api/slaughter`  
**Authorization:** Required

### GET `/slaughter/overview`
Get slaughterhouse dashboard statistics for a specific month.

**Query Parameters:**
- `year` (required): Year (e.g., 2026)
- `month` (required): Month (1-12)

**Response:** `SlaughterOverviewDto`
```json
{
  "totalTransactions": 21,
  "totalHeads": 44,
  "totalCollected": 13011.00,
  "hogCount": 33,
  "carabaoCount": 6,
  "cowCount": 4,
  "othersCount": 1
}
```

**Business Rules:**
- Returns aggregated statistics for all transactions in the specified month
- `othersCount` includes all custom animal types (not Hog/Carabao/Cow)
- `totalCollected` is sum of all transaction amounts

### GET `/slaughter/transactions`
Get all slaughter transactions for a specific month.

**Query Parameters:**
- `year` (required): Year (e.g., 2026)
- `month` (required): Month (1-12)

**Response:** `IReadOnlyList<SlaughterTransactionDto>`
```json
[
  {
    "id": "guid",
    "ownerName": "Diaz, Arnel R.",
    "animalType": 99,
    "customAnimalType": "Goat",
    "numberOfHeads": 4,
    "ratePerHead": 215.25,
    "totalAmount": 861.00,
    "orNumber": "OR-2026-SLH-018",
    "transactionDate": "2026-03-26"
  },
  {
    "id": "guid",
    "ownerName": "Flores, Rudy Q.",
    "animalType": 1,
    "customAnimalType": null,
    "numberOfHeads": 2,
    "ratePerHead": 250.00,
    "totalAmount": 500.00,
    "orNumber": "OR-2026-SLH-017",
    "transactionDate": "2026-03-25"
  }
]
```

**Business Rules:**
- Returns all transactions ordered by date descending
- `animalType` values: 1=Hog, 2=Carabao, 3=Cow, 99=Other
- `customAnimalType` is populated only when `animalType` is 99 (Other)
- `totalAmount` is computed as `ratePerHead × numberOfHeads`

### POST `/slaughter/record`
Record a new slaughter transaction.

**Request Body:** `RecordSlaughterCommand`
```json
{
  "ownerName": "Diaz, Arnel R.",
  "transactionDate": "2026-05-07",
  "orNumber": "OR-2026-SLH-025",
  "animalType": 99,
  "customAnimalType": "Goat",
  "numberOfHeads": 4,
  "customRate": 215.25
}
```

**Response:** `bool` (true on success)

**Business Rules:**
- `animalType` must be 1 (Hog), 2 (Carabao), 3 (Cow), or 99 (Other)
- For standard animals (Hog/Carabao/Cow), rate is automatically applied:
  - Hog: ₱250/head
  - Carabao: ₱365/head
  - Cow: ₱365/head
- For custom animals (`animalType` = 99):
  - `customAnimalType` is required (e.g., "Goat", "Sheep")
  - `customRate` is required and must be > 0
- OR number must be globally unique across all facilities
- `numberOfHeads` must be > 0
- `ownerName` is required (max 100 characters)

**Validation Errors:**
```json
{
  "isSuccess": false,
  "error": "Validation failed",
  "errors": {
    "ORNumber": ["OR number already exists."],
    "CustomAnimalType": ["Custom animal type is required."],
    "CustomRate": ["Custom rate must be greater than 0."]
  }
}
```

### PUT `/slaughter/update`
Update grouped slaughter transactions (for editing existing records).

**Request Body:** `UpdateSlaughterCommand`
```json
{
  "ownerName": "Diaz, Arnel R.",
  "transactionDate": "2026-03-26",
  "orNumber": "OR-2026-SLH-018",
  "animals": [
    {
      "animalType": 1,
      "customAnimalType": null,
      "numberOfHeads": 3,
      "customRate": null
    },
    {
      "animalType": 99,
      "customAnimalType": "Goat",
      "numberOfHeads": 1,
      "customRate": 215.25
    }
  ]
}
```

**Response:** `bool` (true on success)

**Business Rules:**
- Updates all transactions with matching `ownerName`, `transactionDate`, and `orNumber`
- Removes all existing transactions for the group
- Creates new transactions based on `animals` array
- Each animal entry follows same validation rules as record endpoint
- At least one animal type must have `numberOfHeads` > 0
- Total heads across all animals must be > 0
- OR number cannot be changed (part of the group identifier)

**Use Case:**
- Used when editing grouped transactions from the UI
- Allows adding/removing animal types from an existing transaction group
- Maintains transaction grouping by owner/date/OR

---

## 7. Collectors Controller
**Base Route:** `/api/collectors`  
**Authorization:** SuperAdmin only

### GET `/collectors`
Get all collectors.

**Response:** `IReadOnlyList<CollectorListDto>`
```json
[
  {
    "id": "guid",
    "fullName": "Maria Santos",
    "employeeId": "EMP-001",
    "contactNumber": "09171234567",
    "isActive": true,
    "assignedFacilities": ["NPM", "TCC"]
  }
]
```

### GET `/collectors/{id}`
Get collector details with activity summary.

**Response:** `CollectorActivityDto`
```json
{
  "id": "guid",
  "fullName": "Maria Santos",
  "employeeId": "EMP-001",
  "contactNumber": "09171234567",
  "email": "maria.santos@cantilan.gov.ph",
  "isActive": true,
  "assignedFacilities": [
    {
      "facilityCode": "NPM",
      "facilityName": "New Public Market"
    }
  ],
  "totalCollectionsThisMonth": 45,
  "totalAmountCollected": 40500.00,
  "lastCollectionDate": "2025-01-15T14:30:00Z"
}
```

### POST `/collectors`
Create a new collector account.

**Request Body:** `CreateCollectorCommand`
```json
{
  "fullName": "Maria Santos",
  "username": "maria.santos",
  "email": "maria.santos@cantilan.gov.ph",
  "password": "TempPass123!",
  "employeeId": "EMP-001",
  "contactNumber": "09171234567",
  "assignedFacilities": ["NPM", "TCC"]
}
```

**Response:** `CollectorDto`

**Business Rules:**
- Username must be unique
- Email must be unique
- EmployeeId must be unique
- Password must meet security requirements
- MustChangePassword set to true by default
- Creates CollectorFacilityAssignment records for each facility

---

## Common Response Patterns

### Success Response
```json
{
  "value": { /* data */ },
  "isSuccess": true,
  "error": null
}
```

### Error Response
```json
{
  "value": null,
  "isSuccess": false,
  "error": "Error message here"
}
```

### Validation Error Response
```json
{
  "value": null,
  "isSuccess": false,
  "error": "Validation failed",
  "errors": {
    "FieldName": ["Error message 1", "Error message 2"],
    "AnotherField": ["Error message"]
  }
}
```

### HTTP Status Codes
- `200 OK` - Success
- `201 Created` - Resource created
- `204 No Content` - Success with no response body
- `400 Bad Request` - Validation error or business rule violation
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not authorized (wrong role)
- `404 Not Found` - Resource not found
- `409 Conflict` - Duplicate resource (e.g., SuperAdmin already exists)
- `500 Internal Server Error` - Unexpected error

---

## Enums Reference

### FacilityCode
```csharp
NPM = 1,  // New Public Market
TCC = 2,  // Tampak Commercial Center
NCC = 3,  // New Commercial Center
BBQ = 4,  // Barbecue Stand
ICE = 5,  // Iceplant
SLH = 6   // Slaughterhouse
```

### MarketSection (NPM only)
```csharp
VegetableArea = 1,
FishSection = 2,
MeatSection = 3
```

### NccAreaLocation (NCC only)
```csharp
Extension = 1,
Corner = 2
```

### StallStatus
```csharp
Active = 1,
Closed = 2
```

### PaymentStatus
```csharp
Unpaid = 1,
Partial = 2,
Paid = 3
```

### ApplicableFees (Flags enum)
```csharp
BaseRental = 1,
Electricity = 2,
Water = 4,
FishFee = 8
```

### AdminRole
```csharp
SuperAdmin = 1,
Admin = 2
```

### AnimalType (Slaughterhouse)
```csharp
Hog = 1,      // ₱250/head
Carabao = 2,  // ₱365/head
Cow = 3,      // ₱365/head
Other = 99    // Custom animal type with custom rate
```

---

## Not Yet Implemented

The following endpoints are planned but not yet implemented:

- **Dashboard Controller** - Overall system dashboard statistics
- **Reports Controller** - Generate PDF/Excel reports
- **Daily Collections Controller** - NPM daily collection tracking
- **Audit Logs Controller** - View system audit trail
- **Admin Users Controller** - Manage admin accounts (SuperAdmin only)
- **Contracts Controller** - Manage stall contracts separately

---

## Notes

1. **Pagination:** Use cursor-based pagination for large datasets (stalls list)
2. **Soft Delete:** All entities support soft delete (IsDeleted flag)
3. **Audit Trail:** All entities track CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
4. **OR Number Uniqueness:** Validated globally across PaymentRecords and DailyCollections
5. **Payment History:** Always returns 12-month rolling window
6. **Computed Properties:** TotalBill, BalanceDue, AmountPaid are computed in application layer, not stored in DB
7. **Refresh Token Rotation:** Each refresh generates a new refresh token pair
8. **Account Lockout:** 5 failed login attempts = 15 minutes lockout
