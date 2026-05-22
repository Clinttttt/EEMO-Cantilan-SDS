# Implementation Plan: Facility Reports Endpoint

## Overview

This implementation plan creates a comprehensive Facility Reports API endpoint that provides analytics data for all six municipal facilities (NPM, TCC, NCC, BBQ, ICE, SLH). The endpoint aggregates payment records, daily collections, stall occupancy, and revenue trends following Clean Architecture with CQRS pattern using MediatR.

The implementation handles two distinct facility types:
- **Daily collection facilities (NPM)**: Track daily fees (₱30/day) with fish weight charges (₱1/kg)
- **Monthly rental facilities (TCC, NCC, BBQ, ICE, SLH)**: Track monthly payments with utilities

## Tasks

- [x] 1. Create core enums and DTOs
  - [x] 1.1 Create ReportPeriod enum in Domain layer
    - Add `ReportPeriod` enum to `Domain/Enums/ReportPeriod.cs`
    - Define values: Weekly = 1, Monthly = 2, Yearly = 3
    - _Requirements: 1.2, 1.3, 1.4_
  
  - [x] 1.2 Create all DTO records in Application layer
    - Create `FacilityReportsDto` with all summary metrics and nested DTOs
    - Create `RevenueTrendDto` with PeriodLabel and Revenue
    - Create `PaymentStatusDistributionDto` with counts and percentages
    - Create `SectionBreakdownDto` with SectionName, Revenue, Percentage
    - Create `TopStallDto` with StallNumber, OccupantName, Revenue
    - Create `CollectionPerformanceDto` with paid/partial/unpaid counts
    - Place all DTOs in `Application/Dtos/Facilities/` folder
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 2. Create repository interface and registration
  - [x] 2.1 Create IFacilityReportsRepository interface
    - Add interface to `Application/Common/Interface/Persistence/IFacilityReportsRepository.cs`
    - Define `GetFacilityReportsAsync` method signature with all parameters
    - Return type: `Task<FacilityReportsDto>`
    - _Requirements: 9.1, 9.2_
  
  - [x] 2.2 Register repository in Infrastructure DependencyInjection
    - Add `services.AddScoped<IFacilityReportsRepository, FacilityReportsRepository>()` to Infrastructure/DependencyInjection.cs
    - _Requirements: 9.1, 9.2_

- [ ] 3. Implement CQRS query components
  - [x] 3.1 Create GetFacilityReportsQuery record
    - Create `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQuery.cs`
    - Define record with FacilityCode, Period, Year, Month?, WeekNumber? parameters
    - Implement `IRequest<Result<FacilityReportsDto>>`
    - _Requirements: 10.1, 10.4, 1.1_
  
  - [ ] 3.2 Create GetFacilityReportsQueryValidator
    - Create `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQueryValidator.cs`
    - Validate FacilityCode is valid enum value
    - Validate Period is valid enum value
    - Validate Year range (2000 to next year)
    - Add conditional validation: Weekly requires Month (1-12) and WeekNumber (1-5)
    - Add conditional validation: Monthly requires Month (1-12)
    - _Requirements: 10.3, 10.5, 10.6, 1.2, 1.3, 1.4, 1.6_
  
  - [ ] 3.3 Create GetFacilityReportsQueryHandler
    - Create `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQueryHandler.cs`
    - Inject `IFacilityReportsRepository` and `IFacilityRepository`
    - Verify facility exists using `IFacilityRepository.GetByCodeAsync()`
    - Return `Result<FacilityReportsDto>.NotFound()` if facility doesn't exist
    - Delegate aggregation to repository
    - Return `Result<FacilityReportsDto>.Success()` with report data
    - _Requirements: 10.2, 10.4, 1.1, 1.5_

- [ ] 4. Checkpoint - Verify CQRS structure
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement repository - Summary metrics calculation
  - [ ] 5.1 Create FacilityReportsRepository class structure
    - Create `Infrastructure/Repositories/FacilityReportsRepository.cs`
    - Inject `AppDbContext`
    - Implement `IFacilityReportsRepository` interface
    - Create private helper methods for date range calculation
    - _Requirements: 9.2, 9.3_
  
  - [ ] 5.2 Implement total revenue calculation
    - For NPM: Sum daily collections (DailyFee + FishKilos * 1.0) where IsPaid = true
    - For NPM: Add monthly payment records (BaseRentalAmount + ElecAmount + WaterAmount + FishKilos * 1.0)
    - For non-NPM: Sum monthly payment records only
    - Filter by date range based on Period, Year, Month, WeekNumber
    - Use `AsNoTracking()` for read-only queries
    - _Requirements: 2.1, 2.8, 2.9, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2, 14.3, 14.4, 14.5, 15.1_
  
  - [ ] 5.3 Implement revenue growth percentage calculation
    - Calculate previous period date range based on Period type
    - Calculate previous period revenue using same logic as current period
    - Calculate growth: ((current - previous) / previous) * 100
    - Handle edge case: previous = 0 returns 0% (not infinity)
    - Handle edge case: previous period doesn't exist returns 0%
    - _Requirements: 2.2, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_
  
  - [ ] 5.4 Implement collection rate calculation
    - Calculate total billed: Sum of all payment records TotalBill for active stalls
    - Calculate amount collected: Sum of AmountPaid for all payment records
    - Calculate rate: (amount collected / total billed) * 100
    - Handle edge case: total billed = 0 returns 0%
    - _Requirements: 2.3_
  
  - [ ] 5.5 Implement collection growth percentage calculation
    - Calculate previous period collection rate using same logic
    - Calculate growth: ((current rate - previous rate) / previous rate) * 100
    - Handle edge cases: previous rate = 0 or doesn't exist returns 0%
    - _Requirements: 2.4, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_
  
  - [ ] 5.6 Implement occupancy and pending payment calculations
    - Count occupied stalls: Stalls with active contracts (IsActive = true)
    - Count pending payments: Payment records with Status = Unpaid or Partial
    - Calculate pending amount: Sum of (TotalBill - AmountPaid) for pending payments
    - Include stalls with no payment record as Unpaid
    - _Requirements: 2.5, 2.6, 2.7, 4.5_

- [ ] 6. Implement repository - Revenue trend generation
  - [ ] 6.1 Implement weekly revenue trend
    - Generate 7 data points for Monday through Sunday
    - Calculate date range for each day in the specified week
    - For NPM: Sum daily collections for each day
    - For all facilities: Include monthly payment records if billing month matches
    - Create `RevenueTrendDto` with day label ("Mon", "Tue", etc.) and revenue
    - _Requirements: 3.1, 3.4, 3.5_
  
  - [ ] 6.2 Implement monthly revenue trend
    - Generate 6 data points for last 6 months
    - Calculate date range for each month
    - Sum all payment records for each month (BillingYear, BillingMonth)
    - For NPM: Include daily collections for each month
    - Create `RevenueTrendDto` with month label ("Jan 2024", "Feb 2024", etc.) and revenue
    - _Requirements: 3.2, 3.4, 3.6_
  
  - [ ] 6.3 Implement yearly revenue trend
    - Generate 5 data points for last 5 years
    - Calculate date range for each year
    - Sum all payment records for each year (BillingYear)
    - For NPM: Include daily collections for each year
    - Create `RevenueTrendDto` with year label ("2024", "2023", etc.) and revenue
    - _Requirements: 3.3, 3.4, 3.7_

- [ ] 7. Implement repository - Payment distribution and section breakdown
  - [ ] 7.1 Implement payment status distribution
    - Query all active stalls for the facility
    - Count stalls with Paid status in selected period
    - Count stalls with Partial status in selected period
    - Count stalls with Unpaid status in selected period
    - Count stalls with no payment record as Unpaid
    - Calculate percentages: (status count / total stalls) * 100
    - Create `PaymentStatusDistributionDto` with counts and percentages
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  
  - [ ] 7.2 Implement section breakdown for NPM
    - Filter stalls where FacilityCode = NPM
    - Group by MarketSection (VegetableArea, FishSection, MeatSection)
    - For each section: Sum daily collections + monthly payments
    - Calculate percentage: (section revenue / total facility revenue) * 100
    - Create `SectionBreakdownDto` list with section name, revenue, percentage
    - _Requirements: 5.1, 5.4, 5.5, 5.6, 13.5_
  
  - [ ] 7.3 Implement section breakdown for NCC
    - Filter stalls where FacilityCode = NCC
    - Group by NccAreaLocation (Corner, Extension)
    - For each area: Sum monthly payments
    - Calculate percentage: (area revenue / total facility revenue) * 100
    - Create `SectionBreakdownDto` list with area name, revenue, percentage
    - _Requirements: 5.2, 5.4, 5.6_
  
  - [ ] 7.4 Handle section breakdown for other facilities
    - For TCC, BBQ, ICE, SLH: Return empty list
    - _Requirements: 5.3_

- [ ] 8. Implement repository - Top stalls and collection performance
  - [ ] 8.1 Implement top revenue stalls identification
    - Query all stalls for the facility with Include(Contract)
    - For NPM: Calculate revenue = daily collections + monthly payments
    - For non-NPM: Calculate revenue = monthly payments only
    - Order by revenue descending
    - Take top 4 stalls
    - For each stall: Get occupant name from active contract or use "Vacant"
    - Create `TopStallDto` list with StallNumber, OccupantName, Revenue
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 13.4, 13.5_
  
  - [ ] 8.2 Implement collection performance calculation
    - Query all active stalls with payment records
    - Count stalls with Status = Paid (fully paid)
    - Count stalls with Status = Partial (partially paid)
    - Count stalls with Status = Unpaid or no payment record (unpaid)
    - For stalls with multiple payment records, use most recent status
    - Create `CollectionPerformanceDto` with counts
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 9. Implement repository - Assemble final DTO
  - [ ] 9.1 Create FacilityReportsDto assembly method
    - Combine all calculated metrics into `FacilityReportsDto`
    - Ensure all IReadOnlyList properties are properly initialized
    - Handle null/empty collections gracefully
    - Return complete DTO with all nested DTOs
    - _Requirements: 1.1, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_
  
  - [ ] 9.2 Add query optimization techniques
    - Use `AsNoTracking()` for all read-only queries
    - Use `Include()` for necessary navigation properties (Stall, Contract, PaymentRecord, DailyCollection)
    - Use `Select()` projections to fetch only required fields
    - Filter soft-deleted entities using IsDeleted = false
    - Ensure indexed columns used in WHERE clauses (FacilityId, BillingYear, BillingMonth, CollectionDate)
    - _Requirements: 9.3, 9.4, 9.5, 9.6, 15.1, 15.2, 15.3, 15.4, 15.5, 15.6_

- [ ] 10. Checkpoint - Verify repository implementation
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Add API controller endpoint
  - [ ] 11.1 Add GetFacilityReports endpoint to FacilitiesController
    - Add `[HttpGet("{facilityCode}/reports")]` endpoint
    - Add `[Authorize]` attribute for authentication
    - Accept route parameter: `FacilityCode facilityCode`
    - Accept query parameters: `ReportPeriod period`, `int year`, `int? month`, `int? weekNumber`
    - Create `GetFacilityReportsQuery` from parameters
    - Send query using MediatR: `await sender.Send(query)`
    - Return `ActionResult<FacilityReportsDto>` using `HandleResponse(result)`
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7_

- [ ] 12. Write unit tests for calculation logic
  - [ ]* 12.1 Write unit tests for growth percentage calculation
    - Test case: Current > Previous returns positive growth
    - Test case: Current < Previous returns negative growth
    - Test case: Previous = 0 returns 0% (not infinity)
    - Test case: Previous = null returns 0%
    - _Requirements: 2.2, 8.6_
  
  - [ ]* 12.2 Write unit tests for collection rate calculation
    - Test case: All paid returns 100%
    - Test case: None paid returns 0%
    - Test case: Mixed status returns correct percentage
    - Test case: No stalls returns 0%
    - _Requirements: 2.3_
  
  - [ ]* 12.3 Write unit tests for revenue aggregation
    - Test case: NPM includes daily collections + monthly payments
    - Test case: Non-NPM includes monthly payments only
    - Test case: Fish fee calculation (FishKilos × ₱1.00)
    - Test case: Utilities calculation (ElecAmount + WaterAmount)
    - _Requirements: 2.8, 2.9, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2, 14.3, 14.4_
  
  - [ ]* 12.4 Write unit tests for payment status distribution
    - Test case: Stalls with no payment record count as Unpaid
    - Test case: Percentage calculation (count / total) × 100
    - Test case: All statuses sum to 100%
    - _Requirements: 4.4, 4.5_
  
  - [ ]* 12.5 Write unit tests for section breakdown
    - Test case: NPM includes VegetableArea, FishSection, MeatSection
    - Test case: NCC includes Corner, Extension
    - Test case: Other facilities return empty list
    - Test case: Percentages sum to 100%
    - _Requirements: 5.1, 5.2, 5.3, 5.6_

- [ ] 13. Write integration tests for end-to-end scenarios
  - [ ]* 13.1 Write integration test for weekly NPM report
    - Seed: 7 days of daily collections + monthly payments
    - Verify: 7 trend data points (Mon-Sun)
    - Verify: Revenue includes both daily and monthly
    - Verify: Section breakdown includes all 3 sections
    - _Requirements: 1.2, 3.1, 13.4, 13.5, 5.1_
  
  - [ ]* 13.2 Write integration test for monthly TCC report
    - Seed: 6 months of payment records
    - Verify: 6 trend data points (last 6 months)
    - Verify: Collection rate calculation
    - Verify: Top 4 stalls by revenue
    - _Requirements: 1.3, 3.2, 2.3, 6.1_
  
  - [ ]* 13.3 Write integration test for yearly NCC report
    - Seed: 5 years of payment records
    - Verify: 5 trend data points (last 5 years)
    - Verify: Section breakdown (Corner vs Extension)
    - Verify: Growth percentage vs previous year
    - _Requirements: 1.4, 3.3, 5.2, 2.2_
  
  - [ ]* 13.4 Write integration test for empty data scenarios
    - Test case: No payment records returns zeros, empty collections
    - Test case: No active stalls returns zeros
    - Test case: No previous period returns growth = 0%
    - _Requirements: 8.4, 8.5_
  
  - [ ]* 13.5 Write integration test for mixed payment statuses
    - Seed: Paid, Partial, Unpaid records
    - Verify: Distribution percentages
    - Verify: Pending amount calculation
    - Verify: Collection performance counts
    - _Requirements: 4.1, 4.2, 4.3, 2.6, 2.7, 7.1, 7.2, 7.3_

- [ ] 14. Final checkpoint and documentation
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Unit tests validate calculation logic and edge cases
- Integration tests validate end-to-end query execution with known data
- The repository handles all complex aggregations to keep the handler thin
- Database-side aggregations using LINQ ensure optimal performance
- No database migrations required - uses existing schema

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["2.2", "3.1", "3.2"] },
    { "id": 3, "tasks": ["3.3"] },
    { "id": 4, "tasks": ["5.1"] },
    { "id": 5, "tasks": ["5.2", "5.3", "5.4", "5.5", "5.6"] },
    { "id": 6, "tasks": ["6.1", "6.2", "6.3"] },
    { "id": 7, "tasks": ["7.1", "7.2", "7.3", "7.4"] },
    { "id": 8, "tasks": ["8.1", "8.2"] },
    { "id": 9, "tasks": ["9.1", "9.2"] },
    { "id": 10, "tasks": ["11.1"] },
    { "id": 11, "tasks": ["12.1", "12.2", "12.3", "12.4", "12.5"] },
    { "id": 12, "tasks": ["13.1", "13.2", "13.3", "13.4", "13.5"] }
  ]
}
```
