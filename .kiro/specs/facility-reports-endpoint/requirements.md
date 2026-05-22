# Requirements Document

## Introduction

This document specifies the requirements for a comprehensive Facility Reports API endpoint that provides analytics data for all six municipal facilities (NPM, TCC, NCC, BBQ, ICE, SLH). The endpoint will aggregate payment records, daily collections, stall occupancy, and revenue trends to support the FacilityReportsModal.razor component with real-time data instead of mock values.

The system must handle two distinct facility types:
- **Daily collection facilities** (NPM): Track daily fees with fish weight charges
- **Monthly rental facilities** (TCC, NCC, BBQ, ICE, SLH): Track monthly payments with utilities

## Glossary

- **Facility_Reports_System**: The backend API endpoint and query handler that aggregates facility analytics
- **Payment_Record**: Monthly payment entity tracking rental, utilities, and fish fees
- **Daily_Collection**: NPM-specific entity tracking daily stall fees
- **Stall**: Physical rental space within a facility
- **Contract**: Agreement between municipality and occupant defining rental terms
- **Market_Section**: NPM subdivision (VegetableArea, FishSection, MeatSection)
- **Ncc_Area_Location**: NCC subdivision (Extension, Corner, Standard)
- **Collection_Rate**: Percentage of expected revenue successfully collected
- **Occupancy_Rate**: Percentage of stalls with active contracts
- **Revenue_Trend**: Time-series data showing revenue over weekly, monthly, or yearly periods
- **Growth_Percentage**: Percentage change from previous period to current period
- **Top_Revenue_Stall**: Stall ranked by highest revenue in the selected period
- **Pending_Payment**: Payment record with status Unpaid or Partial
- **Period_Query**: User-specified time range (Weekly, Monthly, Yearly) with date parameters

## Requirements

### Requirement 1: Query Facility Reports by Period

**User Story:** As an admin, I want to query facility reports for a specific time period, so that I can analyze revenue and collection performance.

#### Acceptance Criteria

1. WHEN a valid facility code and period are provided, THE Facility_Reports_System SHALL return a comprehensive report DTO
2. WHERE the period is Weekly, THE Facility_Reports_System SHALL require year, month, and weekNumber parameters
3. WHERE the period is Monthly, THE Facility_Reports_System SHALL require year and month parameters
4. WHERE the period is Yearly, THE Facility_Reports_System SHALL require only the year parameter
5. IF an invalid facility code is provided, THEN THE Facility_Reports_System SHALL return a 404 error
6. IF required period parameters are missing, THEN THE Facility_Reports_System SHALL return a 400 validation error

### Requirement 2: Calculate Summary Metrics

**User Story:** As an admin, I want to see summary metrics for the selected period, so that I can quickly assess facility performance.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL calculate total revenue for the selected period
2. THE Facility_Reports_System SHALL calculate revenue growth percentage by comparing current period to previous period
3. THE Facility_Reports_System SHALL calculate collection rate as (amount collected / total billed) * 100
4. THE Facility_Reports_System SHALL calculate collection growth percentage by comparing current period collection rate to previous period
5. THE Facility_Reports_System SHALL calculate occupancy as count of stalls with active contracts
6. THE Facility_Reports_System SHALL calculate pending payment count as stalls with Unpaid or Partial status
7. THE Facility_Reports_System SHALL calculate pending payment amount as sum of balance due for pending payments
8. WHEN calculating revenue for NPM, THE Facility_Reports_System SHALL include both daily collections and monthly payment records
9. WHEN calculating revenue for non-NPM facilities, THE Facility_Reports_System SHALL include only monthly payment records

### Requirement 3: Generate Revenue Trend Data

**User Story:** As an admin, I want to see revenue trends over time, so that I can identify patterns and forecast future revenue.

#### Acceptance Criteria

1. WHERE the period is Weekly, THE Facility_Reports_System SHALL return 7 data points representing Monday through Sunday
2. WHERE the period is Monthly, THE Facility_Reports_System SHALL return 6 data points representing the last 6 months
3. WHERE the period is Yearly, THE Facility_Reports_System SHALL return 5 data points representing the last 5 years
4. FOR ALL trend data points, THE Facility_Reports_System SHALL include the period label and revenue amount
5. WHEN calculating weekly revenue for NPM, THE Facility_Reports_System SHALL sum daily collections for each day
6. WHEN calculating monthly revenue, THE Facility_Reports_System SHALL sum all payment records for each month
7. WHEN calculating yearly revenue, THE Facility_Reports_System SHALL sum all payment records for each year

### Requirement 4: Calculate Payment Status Distribution

**User Story:** As an admin, I want to see payment status distribution, so that I can identify collection issues.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL count stalls with Paid status in the selected period
2. THE Facility_Reports_System SHALL count stalls with Partial status in the selected period
3. THE Facility_Reports_System SHALL count stalls with Unpaid status in the selected period
4. THE Facility_Reports_System SHALL calculate percentage for each status as (status count / total stalls) * 100
5. WHEN a stall has no payment record for the period, THE Facility_Reports_System SHALL count it as Unpaid
6. FOR ALL payment status calculations, THE Facility_Reports_System SHALL include only active stalls

### Requirement 5: Generate Section Breakdown for NPM and NCC

**User Story:** As an admin, I want to see revenue breakdown by section, so that I can compare performance across facility areas.

#### Acceptance Criteria

1. WHERE the facility is NPM, THE Facility_Reports_System SHALL return revenue breakdown for VegetableArea, FishSection, and MeatSection
2. WHERE the facility is NCC, THE Facility_Reports_System SHALL return revenue breakdown for Corner and Extension areas
3. WHERE the facility is not NPM or NCC, THE Facility_Reports_System SHALL return an empty section breakdown
4. FOR ALL section breakdown items, THE Facility_Reports_System SHALL include section name, revenue amount, and percentage of total
5. WHEN calculating section revenue for NPM, THE Facility_Reports_System SHALL include both daily collections and monthly payments for stalls in that section
6. WHEN calculating section percentage, THE Facility_Reports_System SHALL use (section revenue / total facility revenue) * 100

### Requirement 6: Identify Top Revenue Stalls

**User Story:** As an admin, I want to see the top revenue-generating stalls, so that I can recognize high-performing occupants.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL return the top 4 stalls ranked by revenue for the selected period
2. FOR ALL top stalls, THE Facility_Reports_System SHALL include stall number, occupant name, and revenue amount
3. WHEN calculating stall revenue for NPM, THE Facility_Reports_System SHALL sum daily collections and monthly payments
4. WHEN calculating stall revenue for non-NPM facilities, THE Facility_Reports_System SHALL sum monthly payments only
5. WHEN a stall has no active contract, THE Facility_Reports_System SHALL use "Vacant" as the occupant name
6. THE Facility_Reports_System SHALL order stalls by revenue in descending order

### Requirement 7: Calculate Collection Performance

**User Story:** As an admin, I want to see collection performance by payment status, so that I can track collection efficiency.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL count stalls with fully paid status
2. THE Facility_Reports_System SHALL count stalls with partial payment status
3. THE Facility_Reports_System SHALL count stalls with unpaid status
4. FOR ALL collection performance counts, THE Facility_Reports_System SHALL include only active stalls
5. WHEN a stall has multiple payment records in the period, THE Facility_Reports_System SHALL use the most recent payment status

### Requirement 8: Handle Previous Period Comparison

**User Story:** As an admin, I want to see growth percentages compared to the previous period, so that I can track performance trends.

#### Acceptance Criteria

1. WHERE the period is Weekly, THE Facility_Reports_System SHALL compare to the previous week
2. WHERE the period is Monthly, THE Facility_Reports_System SHALL compare to the previous month
3. WHERE the period is Yearly, THE Facility_Reports_System SHALL compare to the previous year
4. WHEN the previous period has zero revenue, THE Facility_Reports_System SHALL return 0% growth
5. WHEN the previous period does not exist, THE Facility_Reports_System SHALL return 0% growth
6. THE Facility_Reports_System SHALL calculate growth as ((current - previous) / previous) * 100

### Requirement 9: Implement Repository Pattern for Complex Queries

**User Story:** As a developer, I want to use repository pattern for complex aggregations, so that I can maintain clean architecture.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL define IFacilityReportsRepository interface in Application layer
2. THE Facility_Reports_System SHALL implement FacilityReportsRepository in Infrastructure layer
3. THE Facility_Reports_System SHALL use Entity Framework Core for database queries
4. THE Facility_Reports_System SHALL use LINQ projections to optimize query performance
5. THE Facility_Reports_System SHALL include navigation properties (Stall, Contract, PaymentRecord, DailyCollection) in queries
6. THE Facility_Reports_System SHALL filter soft-deleted entities using IsDeleted flag

### Requirement 10: Follow CQRS Pattern with MediatR

**User Story:** As a developer, I want to use CQRS pattern, so that I can maintain consistent architecture.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL define GetFacilityReportsQuery in Application/Queries/Facilities/GetFacilityReports folder
2. THE Facility_Reports_System SHALL define GetFacilityReportsQueryHandler in the same folder
3. THE Facility_Reports_System SHALL define GetFacilityReportsQueryValidator using FluentValidation
4. THE Facility_Reports_System SHALL return Result<FacilityReportsDto> from the handler
5. THE Facility_Reports_System SHALL validate facility code is a valid enum value
6. THE Facility_Reports_System SHALL validate period is one of Weekly, Monthly, or Yearly

### Requirement 11: Create Comprehensive DTO Structure

**User Story:** As a developer, I want a well-structured DTO, so that the frontend can easily consume the data.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL define FacilityReportsDto with all summary metrics
2. THE Facility_Reports_System SHALL define RevenueTrendDto with period label and amount
3. THE Facility_Reports_System SHALL define PaymentStatusDistributionDto with counts and percentages
4. THE Facility_Reports_System SHALL define SectionBreakdownDto with name, revenue, and percentage
5. THE Facility_Reports_System SHALL define TopStallDto with stall number, occupant, and revenue
6. THE Facility_Reports_System SHALL define CollectionPerformanceDto with paid, partial, and unpaid counts

### Requirement 12: Add API Controller Endpoint

**User Story:** As a frontend developer, I want a RESTful endpoint, so that I can fetch facility reports from the Blazor client.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL add a GET endpoint at /api/facilities/{facilityCode}/reports
2. THE Facility_Reports_System SHALL accept query parameters: period, year, month, weekNumber
3. THE Facility_Reports_System SHALL require [Authorize] attribute for authentication
4. THE Facility_Reports_System SHALL use ApiBaseController.HandleResponse for consistent error handling
5. THE Facility_Reports_System SHALL return 200 OK with FacilityReportsDto on success
6. THE Facility_Reports_System SHALL return 400 Bad Request for validation errors
7. THE Facility_Reports_System SHALL return 404 Not Found for invalid facility codes

### Requirement 13: Handle NPM Daily Collection Aggregation

**User Story:** As an admin, I want NPM reports to include daily collections, so that I can see complete revenue data.

#### Acceptance Criteria

1. WHEN the facility is NPM, THE Facility_Reports_System SHALL query DailyCollection table
2. WHEN the facility is NPM, THE Facility_Reports_System SHALL sum DailyFee and FishFeeAmount for each stall
3. WHEN the facility is NPM, THE Facility_Reports_System SHALL filter daily collections by IsPaid = true
4. WHEN the facility is NPM, THE Facility_Reports_System SHALL include daily collections in total revenue
5. WHEN the facility is NPM, THE Facility_Reports_System SHALL include daily collections in section breakdown
6. WHEN the facility is not NPM, THE Facility_Reports_System SHALL not query DailyCollection table

### Requirement 14: Handle Monthly Rental Aggregation

**User Story:** As an admin, I want monthly rental reports to include all payment components, so that I can see complete billing data.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL sum BaseRentalAmount for all payment records
2. THE Facility_Reports_System SHALL sum ElecAmount for all payment records with electricity fees
3. THE Facility_Reports_System SHALL sum WaterAmount for all payment records with water fees
4. THE Facility_Reports_System SHALL sum FishFeeAmount for NPM fish section payment records
5. THE Facility_Reports_System SHALL calculate total revenue as sum of all fee components
6. THE Facility_Reports_System SHALL filter payment records by BillingYear and BillingMonth matching the period

### Requirement 15: Optimize Query Performance

**User Story:** As a developer, I want optimized queries, so that reports load quickly even with large datasets.

#### Acceptance Criteria

1. THE Facility_Reports_System SHALL use AsNoTracking for read-only queries
2. THE Facility_Reports_System SHALL use Select projections to fetch only required fields
3. THE Facility_Reports_System SHALL use Include for necessary navigation properties
4. THE Facility_Reports_System SHALL execute aggregations in the database using LINQ
5. THE Facility_Reports_System SHALL avoid N+1 query problems by eager loading related entities
6. THE Facility_Reports_System SHALL use indexed columns (FacilityId, BillingYear, BillingMonth, CollectionDate) in WHERE clauses
