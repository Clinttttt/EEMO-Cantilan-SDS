# Implementation Plan

## Overview

This implementation plan follows the exploratory bugfix workflow using the bug condition methodology. The tasks are ordered to:
1. **Explore** - Write tests BEFORE fix to understand the bug (Bug Condition)
2. **Preserve** - Write tests for non-buggy behavior (Preservation Requirements)
3. **Implement** - Apply the fix with understanding (Expected Behavior)
4. **Validate** - Verify fix works and doesn't break anything

## Tasks

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - NPM Daily Collections Excluded from Payment Calculations
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the bug exists
  - **Scoped PBT Approach**: For deterministic bugs, scope the property to the concrete failing case(s) to ensure reproducibility
  - Test implementation details from Bug Condition in design:
    - Create test NPM stall with daily collections (e.g., 5 days paid = ₱150)
    - Set PartialAmount = ₱0 (no monthly payment yet)
    - Load Profile.razor component and observe Payment Record modal
    - Assert that TotalPaid should equal ₱150 (daily collection total)
    - Assert that BalanceDue should equal MonthlyRate - ₱150
  - The test assertions should match the Expected Behavior Properties from design:
    - `TotalPaid = IsPaid ? TotalBill : IsPartial ? PartialAmount + DailyCollectionTotal : DailyCollectionTotal`
    - `BalanceDue = TotalBill - TotalPaid`
  - Test scenarios:
    1. NPM stall with only daily collections (₱150 from 5 days)
    2. NPM stall with partial payment (₱600) + daily collections (₱300) = ₱900 (should auto-upgrade to Paid)
    3. NPM Fish Section with partial (₱500) + daily collections (₱450) + fish fee (₱50) = ₱1,000
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bug exists)
  - Document counterexamples found:
    - Expected: TotalPaid = ₱150, Actual: TotalPaid = ₱0
    - Expected: Status = Paid (auto-upgraded), Actual: Status = Partial
    - Expected: BalanceDue = ₱750, Actual: BalanceDue = ₱900
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5_

- [ ] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Non-NPM Facility Payment Calculations Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - Observe behavior on UNFIXED code for non-buggy inputs:
    - Load TCC facility stall with PartialAmount = ₱1,200, MonthlyRate = ₱2,400
    - Observe: TotalPaid = ₱1,200, BalanceDue = ₱1,200, Status = Partial
    - Load NCC facility stall with IsPaid = true, MonthlyRate = ₱1,200
    - Observe: TotalPaid = ₱1,200, BalanceDue = ₱0, Status = Paid
    - Load NPM stall with zero daily collections, PartialAmount = ₱300
    - Observe: TotalPaid = ₱300, BalanceDue = ₱600, Status = Partial
  - Write property-based tests capturing observed behavior patterns from Preservation Requirements:
    - For all non-NPM facilities (TCC, NCC, BBQ, ICE, SLH): TotalPaid = IsPaid ? TotalBill : IsPartial ? PartialAmount : 0
    - For all non-NPM facilities: BalanceDue = TotalBill - TotalPaid
    - For all NPM stalls with zero daily collections: Same calculation as non-NPM
    - For all facilities: Payment history display remains unchanged
    - For all facilities: Fish fee calculation from monthly records remains unchanged
  - Property-based testing generates many test cases for stronger guarantees
  - Test cases:
    1. TCC facility stalls with various payment states (Paid, Partial, Unpaid)
    2. NCC facility stalls with various payment states
    3. BBQ, ICE, SLH facility stalls with various payment states
    4. NPM stalls with zero daily collections
    5. Fish Section stalls with fish fee calculations
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [ ] 3. Fix for daily collections payment tracking

  - [~] 3.1 Add daily collection properties to StallProfile class
    - Add `decimal DailyCollectionTotal { get; set; }` property
    - Add `int DaysCollected { get; set; }` property (for UI display)
    - Modify `TotalPaid` computed property to include `DailyCollectionTotal`:
      ```csharp
      public decimal TotalPaid => IsPaid ? TotalBill 
                                 : IsPartial ? PartialAmount + DailyCollectionTotal 
                                 : DailyCollectionTotal;
      ```
    - Modify `BalanceDue` computed property to use updated `TotalPaid`:
      ```csharp
      public decimal BalanceDue => TotalBill - TotalPaid;
      ```
    - _Bug_Condition: isBugCondition(input) where input.FacilityCode = NPM AND input.HasDailyCollections = true AND input.IsPaymentRecordModalOpen = true_
    - _Expected_Behavior: TotalPaid = IsPaid ? TotalBill : IsPartial ? PartialAmount + DailyCollectionTotal : DailyCollectionTotal; BalanceDue = TotalBill - TotalPaid_
    - _Preservation: Non-NPM facilities and NPM stalls without daily collections must continue to use existing calculation formulas_
    - _Requirements: 2.1, 2.2, 2.3, 3.2, 3.5_

  - [~] 3.2 Inject IDailyCollectionApiClient dependency
    - Add `@inject IDailyCollectionApiClient DailyCollectionApi` directive at top of Profile.razor
    - Verify that `IDailyCollectionApiClient` interface exists in Application layer
    - Verify that `DailyCollectionApiClient` implementation exists in Infrastructure layer
    - Verify that the API client is registered in DI container
    - _Bug_Condition: Missing API client prevents loading daily collection data_
    - _Expected_Behavior: API client is available for dependency injection_
    - _Preservation: Existing API clients and DI registrations remain unchanged_
    - _Requirements: 2.1_

  - [~] 3.3 Modify LoadStallData() method to load daily collections for NPM facilities
    - After loading current payment record, add conditional logic:
      ```csharp
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
      ```
    - Handle 404 response gracefully (no daily collections = 0 total)
    - For non-NPM facilities, explicitly set `dailyCollectionTotal = 0` (preservation)
    - _Bug_Condition: LoadStallData() does not call GetDailyCollectionMonthAsync for NPM facilities_
    - _Expected_Behavior: LoadStallData() loads daily collection data for NPM facilities using GetDailyCollectionMonthAsync_
    - _Preservation: Non-NPM facilities do not attempt to load daily collections_
    - _Requirements: 2.1, 3.1_

  - [~] 3.4 Update StallProfile initialization to include daily collection data
    - In StallProfile object creation, add:
      ```csharp
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
      ```
    - Ensure `DailyCollectionTotal = 0` for non-NPM facilities (explicit preservation)
    - _Bug_Condition: StallProfile does not store daily collection data_
    - _Expected_Behavior: StallProfile includes DailyCollectionTotal and DaysCollected properties_
    - _Preservation: Non-NPM facilities have DailyCollectionTotal = 0_
    - _Requirements: 2.1, 2.2, 2.3, 3.1_

  - [~] 3.5 Implement auto-upgrade logic from Partial to Paid status
    - After setting `IsPaid` and `IsPartial` from payment record, add:
      ```csharp
      // Auto-upgrade logic
      if (Stall.IsPartial && (Stall.PartialAmount + Stall.DailyCollectionTotal >= Stall.TotalBill))
      {
          Stall.IsPaid = true;
          Stall.IsPartial = false;
      }
      ```
    - This mirrors the domain logic in `PaymentRecord.UpdateStatus()`
    - Only applies to NPM facilities with daily collections
    - _Bug_Condition: Auto-upgrade from Partial to Paid fails when daily collections complete payment_
    - _Expected_Behavior: When PartialAmount + DailyCollectionTotal >= TotalBill, status auto-upgrades to Paid_
    - _Preservation: Non-NPM facilities continue to use existing status logic_
    - _Requirements: 2.5_

  - [~] 3.6 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - NPM Daily Collections Included in Payment Calculations
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - Verify test scenarios:
      1. NPM stall with only daily collections shows correct TotalPaid and BalanceDue
      2. NPM stall with partial + daily collections auto-upgrades to Paid status
      3. NPM Fish Section with all fees calculates correct totals
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed)
    - Document that counterexamples are now resolved:
      - TotalPaid now includes daily collection totals
      - BalanceDue now subtracts daily collections
      - Status auto-upgrades when payment is complete
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [~] 3.7 Verify preservation tests still pass
    - **Property 2: Preservation** - Non-NPM Facility Payment Calculations Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - Verify test scenarios:
      1. TCC facility stalls calculate payments correctly (unchanged)
      2. NCC facility stalls calculate payments correctly (unchanged)
      3. BBQ, ICE, SLH facility stalls calculate payments correctly (unchanged)
      4. NPM stalls with zero daily collections work correctly (unchanged)
      5. Fish fee calculations remain correct (unchanged)
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix (no regressions)
    - Document that preservation requirements are satisfied:
      - Non-NPM facilities unchanged
      - NPM stalls without daily collections unchanged
      - Payment history display unchanged
      - Fish fee calculation unchanged
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [~] 4. Checkpoint - Ensure all tests pass
  - Run all bug condition exploration tests (Property 1)
  - Run all preservation property tests (Property 2)
  - Verify no test failures
  - Verify no regressions in existing functionality
  - Test manually in browser:
    1. Navigate to NPM facility
    2. Open Payment Record modal for stall with daily collections
    3. Verify Total Paid includes daily collection total
    4. Verify Balance Due is calculated correctly
    5. Verify status shows "Paid" when payment is complete
    6. Navigate to TCC facility and verify payment calculations unchanged
  - If any issues arise, ask the user for guidance
  - Document final verification results

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1"] },
    { "id": 1, "tasks": ["2"] },
    { "id": 2, "tasks": ["3.1", "3.2"] },
    { "id": 3, "tasks": ["3.3", "3.4"] },
    { "id": 4, "tasks": ["3.5"] },
    { "id": 5, "tasks": ["3.6", "3.7"] },
    { "id": 6, "tasks": ["4"] }
  ]
}
```

**Dependency Rules:**
- Wave 0: Task 1 (Bug Condition Exploration Test) - Must run first to understand the bug
- Wave 1: Task 2 (Preservation Property Tests) - Establish baseline behavior after understanding bug
- Wave 2: Tasks 3.1-3.2 (Add properties and inject API client) - Foundation for fix
- Wave 3: Tasks 3.3-3.4 (Load data and initialize profile) - Core implementation
- Wave 4: Task 3.5 (Auto-upgrade logic) - Final implementation step
- Wave 5: Tasks 3.6-3.7 (Verify tests pass) - Validation that fix works and preserves behavior
- Wave 6: Task 4 (Final checkpoint) - Complete verification

## Notes

- This bugfix uses the bug condition methodology to ensure systematic validation
- Tests are written BEFORE the fix to surface counterexamples and understand the bug
- Preservation tests ensure non-NPM facilities remain completely unchanged
- Auto-upgrade logic mirrors the domain method `PaymentRecord.UpdateStatus()`
- All fee rates and business rules are defined in `FeeRates` constants
