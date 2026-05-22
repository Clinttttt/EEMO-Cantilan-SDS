# Daily Collections Payment Tracking Fix - Bugfix Design

## Overview

The Payment Record modal in Profile.razor (NPM facility) currently excludes daily collection payments (₱30/day) when calculating Total Paid and Balance Due. This causes incorrect payment tracking and prevents proper status upgrades from Partial to Paid when daily collections should complete the payment.

This fix will integrate daily collection data into the payment calculations by:
1. Loading daily collection data alongside payment records for NPM facilities
2. Including daily collection totals in Total Paid and Balance Due calculations
3. Implementing auto-upgrade logic from Partial to Paid when combined payments meet or exceed the total bill
4. Ensuring non-NPM facilities remain completely unchanged (regression prevention)

**Impact:** Accurate payment tracking for NPM stalls with daily collections, proper status transitions, and correct balance calculations.

## Glossary

- **Bug_Condition (C)**: The condition that triggers the bug - when NPM facility stalls with daily collections view the Payment Record modal
- **Property (P)**: The desired behavior - Total Paid and Balance Due must include daily collection totals
- **Preservation**: Existing behavior for non-NPM facilities and stalls without daily collections that must remain unchanged
- **LoadStallData()**: The method in `Profile.razor` that loads stall and payment data from the API
- **DailyCollectionMonthDto**: The DTO containing aggregated daily collection data for a specific month
- **StallProfile**: The local class in Profile.razor that holds all stall display data
- **IDailyCollectionApiClient**: The API client interface for fetching daily collection data
- **FacilityCode**: Enum identifying facility types (NPM=1, TCC=2, NCC=3, BBQ=4, ICE=5, SLH=6)
- **PaymentStatus**: Enum for payment states (Unpaid=0, Partial=1, Paid=2)

## Bug Details

### Bug Condition

The bug manifests when an NPM facility stall with daily collections is viewed in the Payment Record modal. The `LoadStallData()` method loads only `PaymentRecord` data and ignores the `DailyCollection` table entries, causing incorrect Total Paid and Balance Due calculations.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type StallPaymentContext
  OUTPUT: boolean
  
  RETURN input.FacilityCode = NPM 
         AND input.HasDailyCollections = true 
         AND input.IsPaymentRecordModalOpen = true
END FUNCTION
```

### Examples

- **Example 1**: Stall "Adan Bohemian" (NPM), Monthly Rate ₱900, 1 day paid (₱30)
  - **Current (WRONG)**: Total Paid = ₱0, Balance Due = ₱900
  - **Expected (CORRECT)**: Total Paid = ₱30, Balance Due = ₱870

- **Example 2**: NPM Fish Section stall, Monthly Rate ₱900, 10 days paid (₱300), Partial payment ₱600
  - **Current (WRONG)**: Total Paid = ₱600, Balance Due = ₱300
  - **Expected (CORRECT)**: Total Paid = ₱900, Balance Due = ₱0, Status = Paid (auto-upgraded)

- **Example 3**: NPM stall, Monthly Rate ₱900, 15 days paid (₱450), Fish fee ₱50, Partial payment ₱500
  - **Current (WRONG)**: Total Paid = ₱500, Balance Due = ₱450
  - **Expected (CORRECT)**: Total Paid = ₱950, Balance Due = ₱0, Status = Paid (auto-upgraded)

- **Edge Case**: NPM stall with 0 daily collections recorded
  - **Expected**: Total Paid = PartialAmount (if any), Balance Due = TotalBill - PartialAmount (no change from current behavior)

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- TCC, NCC, BBQ, ICE, and SLH facilities must continue to calculate payments using only PaymentRecord data
- Stalls without any daily collections must continue to use existing Total Paid formula
- Payment history display in the 12-month grid must remain unchanged
- Fish fee calculation from monthly payment records must continue to work
- Helper methods `GetDisplayPartialAmount()` and `GetDisplayBalanceRemaining()` must continue to format values correctly
- OR Number, Collector Name, and Remarks display must continue to show monthly PaymentRecord data
- Electricity and Water utility amounts must continue to load from PaymentRecord

**Scope:**
All inputs that do NOT involve NPM facilities with daily collections should be completely unaffected by this fix. This includes:
- All non-NPM facility stalls (TCC, NCC, BBQ, ICE, SLH)
- NPM stalls with zero daily collection records
- Payment history visualization and statistics
- Edit modal functionality
- Record Payment modal functionality (separate from Payment Record display)

## Hypothesized Root Cause

Based on the bug description and code analysis, the root causes are:

1. **Missing Data Loading**: The `LoadStallData()` method does not call `IDailyCollectionApiClient.GetDailyCollectionMonthAsync()` to fetch daily collection data for NPM facilities

2. **Incomplete Calculation Logic**: The `StallProfile` class computes `TotalPaid` and `BalanceDue` using only `PartialAmount` from monthly payment records, without including daily collection totals

3. **No Facility-Specific Logic**: The method treats all facilities identically, without conditional logic to handle NPM's unique daily collection system

4. **Missing Auto-Upgrade Logic**: The UI does not implement the auto-upgrade check that exists in `PaymentRecord.UpdateStatus()` domain method

## Correctness Properties

Property 1: Bug Condition - Daily Collections Included in Payment Calculations

_For any_ NPM facility stall where daily collections exist for the current month, the Profile.razor component SHALL include the daily collection total in Total Paid and Balance Due calculations, such that Total Paid = (IsPaid ? TotalBill : IsPartial ? PartialAmount + DailyCollectionTotal : DailyCollectionTotal) and Balance Due = TotalBill - Total Paid.

**Validates: Requirements 2.1, 2.2, 2.3**

Property 2: Preservation - Non-NPM Facility Behavior

_For any_ stall in non-NPM facilities (TCC, NCC, BBQ, ICE, SLH) or NPM stalls without daily collections, the Profile.razor component SHALL produce exactly the same Total Paid and Balance Due calculations as the original code, preserving all existing payment tracking behavior.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `EEMOCantilanSDS.Client\Components\Pages\Shared\Actions\Profile.razor`

**Class**: `StallProfile` (local class)

**Specific Changes**:

1. **Add Daily Collection Properties**: Extend `StallProfile` class to store daily collection data
   - Add `decimal DailyCollectionTotal { get; set; }` property
   - Add `int DaysCollected { get; set; }` property (for UI display)
   - Modify `TotalPaid` computed property to include `DailyCollectionTotal`
   - Modify `BalanceDue` computed property to use updated `TotalPaid`

2. **Inject IDailyCollectionApiClient**: Add dependency injection for daily collection API client
   - Add `@inject IDailyCollectionApiClient DailyCollectionApi` directive at top of file

3. **Modify LoadStallData() Method**: Add conditional logic to load daily collections for NPM facilities
   - After loading current payment record, check if `facilityCode == FacilityCode.NPM`
   - If NPM, call `DailyCollectionApi.GetDailyCollectionMonthAsync(stallDto.Id, DateTime.Now.Year, DateTime.Now.Month)`
   - Extract `GrandTotal` from `DailyCollectionMonthDto` and assign to `StallProfile.DailyCollectionTotal`
   - Extract `DaysCollected` for potential UI display
   - Handle 404 response gracefully (no daily collections = 0 total)

4. **Update StallProfile Initialization**: Include daily collection total in profile object creation
   - Set `DailyCollectionTotal = dailyCollectionResult?.GrandTotal ?? 0` for NPM facilities
   - Set `DailyCollectionTotal = 0` for non-NPM facilities (explicit preservation)

5. **Implement Auto-Upgrade Logic**: Add status upgrade check after loading payment data
   - After setting `IsPaid` and `IsPartial` from payment record, check if `IsPartial && (PartialAmount + DailyCollectionTotal >= TotalBill)`
   - If true, override `IsPaid = true` and `IsPartial = false` to reflect auto-upgraded status
   - This mirrors the domain logic in `PaymentRecord.UpdateStatus()`

### Implementation Pseudocode

```csharp
// 1. Add to StallProfile class
class StallProfile
{
    // ... existing properties ...
    public decimal DailyCollectionTotal { get; set; }
    public int DaysCollected { get; set; }
    
    // Modified computed properties
    public decimal TotalPaid => IsPaid ? TotalBill 
                               : IsPartial ? PartialAmount + DailyCollectionTotal 
                               : DailyCollectionTotal;
    
    public decimal BalanceDue => TotalBill - TotalPaid;
}

// 2. Inject API client
@inject IDailyCollectionApiClient DailyCollectionApi

// 3. Modify LoadStallData() method
async Task LoadStallData()
{
    // ... existing code to load stall and payment data ...
    
    // NEW: Load daily collections for NPM facilities
    decimal dailyCollectionTotal = 0;
    int daysCollected = 0;
    
    if (facilityCode == Domain.Enums.FacilityCode.NPM)
    {
        var dailyCollectionResult = await DailyCollectionApi.GetDailyCollectionMonthAsync(
            stallDto.Id,
            DateTime.Now.Year,
            DateTime.Now.Month);
        
        if (dailyCollectionResult.IsSuccess && dailyCollectionResult.Value != null)
        {
            dailyCollectionTotal = dailyCollectionResult.Value.GrandTotal;
            daysCollected = dailyCollectionResult.Value.DaysCollected;
        }
    }
    
    // ... existing code to create StallProfile ...
    
    Stall = new()
    {
        // ... existing property assignments ...
        DailyCollectionTotal = dailyCollectionTotal,
        DaysCollected = daysCollected,
        IsPaid = isPaid,
        IsPartial = isPartial,
        PartialAmount = partialAmount,
        // ... rest of properties ...
    };
    
    // NEW: Auto-upgrade logic
    if (Stall.IsPartial && (Stall.PartialAmount + Stall.DailyCollectionTotal >= Stall.TotalBill))
    {
        Stall.IsPaid = true;
        Stall.IsPartial = false;
    }
}
```

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Create test scenarios with NPM stalls that have daily collections, then observe the Profile.razor component displaying incorrect Total Paid and Balance Due values. Run these tests on the UNFIXED code to observe failures and understand the root cause.

**Test Cases**:
1. **NPM Stall with Daily Collections Only**: Create NPM stall with 5 days paid (₱150), no monthly payment (will fail on unfixed code - shows ₱0 paid)
2. **NPM Stall with Partial + Daily Collections**: Create NPM stall with ₱600 partial payment + 10 days (₱300) = ₱900 total (will fail on unfixed code - shows ₱600 paid, doesn't auto-upgrade to Paid)
3. **NPM Fish Section with All Fees**: Create NPM Fish stall with ₱500 partial + 15 days (₱450) + ₱50 fish fee (will fail on unfixed code - shows ₱500 paid instead of ₱950)
4. **NPM Stall with Zero Daily Collections**: Create NPM stall with ₱0 daily collections, ₱300 partial (may pass on unfixed code - edge case)

**Expected Counterexamples**:
- Total Paid displays only PartialAmount, ignoring daily collection totals
- Balance Due is calculated incorrectly, not subtracting daily collections
- Status remains "Partial" even when PartialAmount + DailyCollectionTotal >= TotalBill
- Possible causes: missing API call, incomplete calculation logic, no auto-upgrade implementation

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := LoadStallData'(input)
  dailyTotal := GetDailyCollectionTotal(input.StallId, input.Year, input.Month)
  
  ASSERT result.TotalPaid = (
    IF result.IsPaid THEN result.TotalBill
    ELSE IF result.IsPartial THEN result.PartialAmount + dailyTotal
    ELSE dailyTotal
  )
  
  ASSERT result.BalanceDue = result.TotalBill - result.TotalPaid
  
  // Auto-upgrade check
  IF result.IsPartial AND (result.PartialAmount + dailyTotal >= result.TotalBill) THEN
    ASSERT result.IsPaid = true
    ASSERT result.IsPartial = false
  END IF
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  resultOriginal := LoadStallData(input)
  resultFixed := LoadStallData'(input)
  
  ASSERT resultOriginal.TotalPaid = resultFixed.TotalPaid
  ASSERT resultOriginal.BalanceDue = resultFixed.BalanceDue
  ASSERT resultOriginal.IsPaid = resultFixed.IsPaid
  ASSERT resultOriginal.IsPartial = resultFixed.IsPartial
  ASSERT resultOriginal.DailyCollectionTotal = 0 // Explicit check for non-NPM
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for non-NPM facilities and NPM stalls without daily collections, then write property-based tests capturing that behavior.

**Test Cases**:
1. **TCC Facility Preservation**: Observe that TCC stalls calculate payments correctly on unfixed code, then write test to verify this continues after fix
2. **NCC Facility Preservation**: Observe that NCC stalls calculate payments correctly on unfixed code, then write test to verify this continues after fix
3. **BBQ, ICE, SLH Preservation**: Observe that other facilities calculate payments correctly on unfixed code, then write test to verify this continues after fix
4. **NPM Stall with Zero Daily Collections**: Observe that NPM stalls without daily collections work correctly on unfixed code, then write test to verify this continues after fix

### Unit Tests

- Test `LoadStallData()` with NPM facility and daily collections present
- Test `LoadStallData()` with NPM facility and zero daily collections
- Test `LoadStallData()` with non-NPM facilities (TCC, NCC, BBQ, ICE, SLH)
- Test `StallProfile.TotalPaid` computed property with various combinations of IsPaid, IsPartial, PartialAmount, and DailyCollectionTotal
- Test `StallProfile.BalanceDue` computed property accuracy
- Test auto-upgrade logic when PartialAmount + DailyCollectionTotal >= TotalBill
- Test auto-upgrade logic when PartialAmount + DailyCollectionTotal < TotalBill (should remain Partial)

### Property-Based Tests

- Generate random NPM stalls with varying daily collection totals and verify Total Paid includes daily collections
- Generate random non-NPM stalls and verify Total Paid calculation remains unchanged
- Generate random payment states (Paid, Partial, Unpaid) with daily collections and verify Balance Due accuracy
- Test that all non-NPM facilities continue to work correctly across many scenarios

### Integration Tests

- Test full Profile.razor component rendering with NPM stall that has daily collections
- Test that Payment Record modal displays correct Total Paid and Balance Due
- Test that status pill displays "Paid" when auto-upgraded from Partial
- Test that non-NPM facility profiles continue to render correctly
- Test navigation from facility list to profile page with daily collections
