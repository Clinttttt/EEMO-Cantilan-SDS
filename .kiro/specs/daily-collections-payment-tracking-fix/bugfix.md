# Bugfix Requirements Document

## Introduction

The Payment Record modal in Profile.razor (NPM facility) does not include daily collection payments (₱30/day) when calculating Total Paid and Balance Due. This causes incorrect payment tracking and prevents proper status upgrades from Partial to Paid.

**Impact:**
- Payment Record modal displays incorrect Total Paid and Balance Due amounts
- Auto-upgrade from Partial to Paid status fails when daily collections should complete payment
- Users cannot see accurate payment progress
- System calculates wrong totals when completing payments (e.g., ₱900 + ₱900 + ₱30 = ₱1,830 instead of recognizing ₱30 already paid)

**Example Scenario:**
- Stall: Adan Bohemian (NPM facility)
- Monthly Rate: ₱900
- Day 1: User marks daily collection checkbox, pays ₱30
- User opens Payment Record modal
- **Current (WRONG)**: Shows "Total Paid = ₱0, Balance Due = ₱900"
- **Expected (CORRECT)**: Should show "Total Paid = ₱30, Balance Due = ₱870"

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the `LoadStallData()` method loads payment data for a stall with daily collections THEN the system loads only `PaymentRecord` data and ignores `DailyCollection` table entries

1.2 WHEN the Payment Record modal calculates Total Paid for a stall with daily collections THEN the system uses only `PartialAmount` from monthly payment records and excludes daily collection totals

1.3 WHEN the Payment Record modal calculates Balance Due for a stall with daily collections THEN the system computes `TotalBill - PartialAmount` without subtracting daily collection payments

1.4 WHEN a user completes a payment after making daily collections THEN the system adds the new payment amount to the total without recognizing previously paid daily collections (e.g., ₱900 + ₱900 + ₱30 = ₱1,830)

1.5 WHEN the system evaluates auto-upgrade from Partial to Paid status THEN the system compares only `PartialAmount >= TotalBill` without including daily collection totals

### Expected Behavior (Correct)

2.1 WHEN the `LoadStallData()` method loads payment data for a stall with daily collections THEN the system SHALL load both `PaymentRecord` data AND daily collection data from the `DailyCollection` table for the current month using `IDailyCollectionApiClient.GetDailyCollectionMonthAsync()`

2.2 WHEN the Payment Record modal calculates Total Paid for a stall with daily collections THEN the system SHALL include daily collection totals in the calculation: `TotalPaid = IsPaid ? TotalBill : IsPartial ? PartialAmount + DailyCollectionTotal : DailyCollectionTotal`

2.3 WHEN the Payment Record modal calculates Balance Due for a stall with daily collections THEN the system SHALL compute `BalanceDue = TotalBill - (PartialAmount + DailyCollectionTotal)`

2.4 WHEN a user completes a payment after making daily collections THEN the system SHALL recognize previously paid daily collections and calculate the correct remaining balance

2.5 WHEN the system evaluates auto-upgrade from Partial to Paid status THEN the system SHALL compare `(PartialAmount + DailyCollectionTotal) >= TotalBill` to determine if the payment is complete

### Unchanged Behavior (Regression Prevention)

3.1 WHEN loading payment data for stalls in non-NPM facilities (TCC, NCC, BBQ, ICE, SLH) THEN the system SHALL CONTINUE TO load only `PaymentRecord` data without attempting to load daily collections

3.2 WHEN calculating Total Paid for stalls without any daily collections THEN the system SHALL CONTINUE TO use the existing formula: `TotalPaid = IsPaid ? TotalBill : IsPartial ? PartialAmount : 0`

3.3 WHEN displaying payment history in the Payment Record modal THEN the system SHALL CONTINUE TO show the existing payment history from `PaymentRecord` table

3.4 WHEN the `StallProfile` class computes `FishFee` for Fish Section stalls THEN the system SHALL CONTINUE TO calculate fish fees from monthly payment records as `FishKilos * 1.00m`

3.5 WHEN helper methods `GetDisplayPartialAmount()` and `GetDisplayBalanceRemaining()` format currency values THEN the system SHALL CONTINUE TO format values correctly after including daily collection totals

3.6 WHEN the Payment Record modal displays OR Number, Collector Name, and Remarks THEN the system SHALL CONTINUE TO display these fields from the monthly `PaymentRecord` data

3.7 WHEN loading stall data for facilities with electricity and water utilities THEN the system SHALL CONTINUE TO load and display `ElecAmount` and `WaterAmount` from `PaymentRecord`

## Bug Condition Derivation

### Bug Condition Function

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type StallPaymentContext
  OUTPUT: boolean
  
  // Bug occurs when:
  // 1. Stall is in NPM facility (has daily collections)
  // 2. Daily collections exist for current month
  // 3. Payment Record modal is being displayed
  RETURN X.FacilityCode = NPM 
         AND X.HasDailyCollections = true 
         AND X.IsPaymentRecordModalOpen = true
END FUNCTION
```

### Property Specification - Fix Checking

```pascal
// Property: Daily Collections Included in Total Paid
FOR ALL X WHERE isBugCondition(X) DO
  result ← LoadStallData'(X)
  dailyTotal ← GetDailyCollectionTotal(X.StallId, X.Year, X.Month)
  
  ASSERT result.TotalPaid = (
    IF result.IsPaid THEN result.TotalBill
    ELSE IF result.IsPartial THEN result.PartialAmount + dailyTotal
    ELSE dailyTotal
  )
  
  ASSERT result.BalanceDue = result.TotalBill - result.TotalPaid
  
  // Auto-upgrade check
  IF result.IsPartial AND (result.PartialAmount + dailyTotal >= result.TotalBill) THEN
    ASSERT result.Status = Paid
  END IF
END FOR
```

### Property Specification - Preservation Checking

```pascal
// Property: Non-NPM Facilities Unchanged
FOR ALL X WHERE NOT isBugCondition(X) DO
  resultOriginal ← LoadStallData(X)
  resultFixed ← LoadStallData'(X)
  
  ASSERT resultOriginal.TotalPaid = resultFixed.TotalPaid
  ASSERT resultOriginal.BalanceDue = resultFixed.BalanceDue
  ASSERT resultOriginal.IsPaid = resultFixed.IsPaid
  ASSERT resultOriginal.IsPartial = resultFixed.IsPartial
END FOR
```

**Key Definitions:**
- **F (LoadStallData)**: The original method that loads only `PaymentRecord` data
- **F' (LoadStallData')**: The fixed method that loads both `PaymentRecord` and `DailyCollection` data
- **C(X)**: Bug condition - NPM facility with daily collections viewing Payment Record modal
- **¬C(X)**: Non-buggy inputs - non-NPM facilities or stalls without daily collections

### Counterexample

**Concrete Example Demonstrating the Bug:**

```
Input:
  StallId: Adan Bohemian (NPM facility)
  MonthlyRate: ₱900
  DailyCollections: 1 day paid (₱30)
  PartialAmount: ₱0
  
Current Behavior (F):
  TotalPaid = ₱0
  BalanceDue = ₱900
  Status = Unpaid
  
Expected Behavior (F'):
  TotalPaid = ₱30
  BalanceDue = ₱870
  Status = Partial (or Unpaid with daily collections shown)
```
