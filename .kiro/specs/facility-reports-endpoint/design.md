# Design Document: Facility Reports Endpoint

## Overview

The Facility Reports API endpoint provides comprehensive analytics data for all six municipal facilities (NPM, TCC, NCC, BBQ, ICE, SLH). This endpoint aggregates payment records, daily collections, stall occupancy, and revenue trends to support the FacilityReportsModal.razor component with real-time data.

The system handles two distinct facility types:
- **Daily collection facilities (NPM)**: Track daily fees (₱30/day) with fish weight charges (₱1/kg)
- **Monthly rental facilities (TCC, NCC, BBQ, ICE, SLH)**: Track monthly payments with utilities

The implementation follows Clean Architecture with CQRS pattern using MediatR, repository pattern for complex aggregations, and Entity Framework Core with PostgreSQL.

## Architecture

### Layer Organization

Following Clean Architecture principles with strict dependency flow:

```
API Layer (FacilitiesController)
    ↓
Application Layer (GetFacilityReportsQuery + Handler + Validator)
    ↓
Domain Layer (Entities, Enums, Result<T>)
    ↑
Infrastructure Layer (FacilityReportsRepository + AppDbContext)
```

### CQRS Pattern

**Query:** `GetFacilityReportsQuery`
- Input: FacilityCode, Period (Weekly/Monthly/Yearly), Year, Month?, WeekNumber?
- Output: `Result<FacilityReportsDto>`
- Handler: `GetFacilityReportsQueryHandler`
- Validator: `GetFacilityReportsQueryValidator`

**Repository:** `IFacilityReportsRepository`
- Complex aggregations performed in repository layer
- Returns DTOs directly (not entities)
- Uses EF Core projections for performance

### Technology Stack

- **Framework**: .NET 9, C# 13
- **Database**: PostgreSQL via Npgsql
- **ORM**: Entity Framework Core 9
- **CQRS**: MediatR
- **Validation**: FluentValidation
- **API**: ASP.NET Core 9 with JWT authentication

## Components and Interfaces

### 1. Query Definition

**Location:** `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQuery.cs`

```csharp
public record GetFacilityReportsQuery(
    FacilityCode FacilityCode,
    ReportPeriod Period,
    int Year,
    int? Month,
    int? WeekNumber
) : IRequest<Result<FacilityReportsDto>>;
```

**ReportPeriod Enum:**
```csharp
public enum ReportPeriod
{
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}
```

### 2. Query Handler

**Location:** `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQueryHandler.cs`

```csharp
public class GetFacilityReportsQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IFacilityRepository facilityRepository
) : IRequestHandler<GetFacilityReportsQuery, Result<FacilityReportsDto>>
{
    public async Task<Result<FacilityReportsDto>> Handle(
        GetFacilityReportsQuery request, 
        CancellationToken ct)
    {
        // 1. Verify facility exists
        var facility = await facilityRepository.GetByCodeAsync(request.FacilityCode, ct);
        if (facility == null)
            return Result<FacilityReportsDto>.NotFound();
        
        // 2. Delegate to repository for complex aggregation
        var report = await reportsRepository.GetFacilityReportsAsync(
            request.FacilityCode,
            request.Period,
            request.Year,
            request.Month,
            request.WeekNumber,
            ct
        );
        
        return Result<FacilityReportsDto>.Success(report);
    }
}
```

### 3. Query Validator

**Location:** `Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQueryValidator.cs`

```csharp
public class GetFacilityReportsQueryValidator : AbstractValidator<GetFacilityReportsQuery>
{
    public GetFacilityReportsQueryValidator()
    {
        RuleFor(x => x.FacilityCode)
            .IsInEnum()
            .WithMessage("Invalid facility code");
        
        RuleFor(x => x.Period)
            .IsInEnum()
            .WithMessage("Period must be Weekly, Monthly, or Yearly");
        
        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(DateTime.UtcNow.Year + 1)
            .WithMessage("Year must be between 2000 and next year");
        
        // Weekly requires month and weekNumber
        When(x => x.Period == ReportPeriod.Weekly, () =>
        {
            RuleFor(x => x.Month)
                .NotNull()
                .InclusiveBetween(1, 12)
                .WithMessage("Month is required for weekly reports (1-12)");
            
            RuleFor(x => x.WeekNumber)
                .NotNull()
                .InclusiveBetween(1, 5)
                .WithMessage("Week number is required for weekly reports (1-5)");
        });
        
        // Monthly requires month
        When(x => x.Period == ReportPeriod.Monthly, () =>
        {
            RuleFor(x => x.Month)
                .NotNull()
                .InclusiveBetween(1, 12)
                .WithMessage("Month is required for monthly reports (1-12)");
        });
    }
}
```

### 4. Repository Interface

**Location:** `Application/Common/Interface/Persistence/IFacilityReportsRepository.cs`

```csharp
public interface IFacilityReportsRepository
{
    Task<FacilityReportsDto> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber,
        CancellationToken ct
    );
}
```

### 5. Repository Implementation

**Location:** `Infrastructure/Repositories/FacilityReportsRepository.cs`

The repository handles all complex aggregations:
- Summary metrics calculation (revenue, growth, collection rate, occupancy)
- Revenue trend data generation (7 days, 6 months, or 5 years)
- Payment status distribution
- Section breakdown (NPM and NCC only)
- Top revenue stalls identification
- Previous period comparison for growth calculations

**Key Implementation Details:**
- Uses `AsNoTracking()` for read-only queries
- Uses `Include()` for necessary navigation properties
- Performs aggregations in database using LINQ
- Returns DTOs directly (not entities)
- Handles NPM daily collections separately from monthly payments
- Recalculates computed properties inline (TotalBill, FishFeeAmount)

### 6. API Controller Endpoint

**Location:** `Api/Controllers/FacilitiesController.cs`

```csharp
[HttpGet("{facilityCode}/reports")]
[Authorize]
public async Task<ActionResult<FacilityReportsDto>> GetFacilityReports(
    [FromRoute] FacilityCode facilityCode,
    [FromQuery] ReportPeriod period,
    [FromQuery] int year,
    [FromQuery] int? month = null,
    [FromQuery] int? weekNumber = null)
{
    var query = new GetFacilityReportsQuery(facilityCode, period, year, month, weekNumber);
    var result = await sender.Send(query);
    return HandleResponse(result);
}
```

**Endpoint:** `GET /api/facilities/{facilityCode}/reports`

**Query Parameters:**
- `period` (required): Weekly, Monthly, or Yearly
- `year` (required): 4-digit year
- `month` (optional): 1-12, required for Weekly and Monthly
- `weekNumber` (optional): 1-5, required for Weekly

**Example Requests:**
```
GET /api/facilities/NPM/reports?period=Weekly&year=2024&month=1&weekNumber=3
GET /api/facilities/TCC/reports?period=Monthly&year=2024&month=6
GET /api/facilities/NCC/reports?period=Yearly&year=2024
```

## Data Models

### Primary DTO Structure

**Location:** `Application/Dtos/Facilities/FacilityReportsDto.cs`

```csharp
public record FacilityReportsDto(
    // Summary Metrics
    decimal TotalRevenue,
    decimal RevenueGrowthPercentage,
    decimal CollectionRate,
    decimal CollectionGrowthPercentage,
    int OccupiedStalls,
    int PendingPaymentCount,
    decimal PendingPaymentAmount,
    
    // Trend Data
    IReadOnlyList<RevenueTrendDto> RevenueTrend,
    
    // Payment Distribution
    PaymentStatusDistributionDto PaymentDistribution,
    
    // Section Breakdown (NPM and NCC only)
    IReadOnlyList<SectionBreakdownDto> SectionBreakdown,
    
    // Top Performers
    IReadOnlyList<TopStallDto> TopStalls,
    
    // Collection Performance
    CollectionPerformanceDto CollectionPerformance
);
```

### Supporting DTOs

**RevenueTrendDto:**
```csharp
public record RevenueTrendDto(
    string PeriodLabel,  // "Mon", "Jan 2024", "2024"
    decimal Revenue
);
```

**PaymentStatusDistributionDto:**
```csharp
public record PaymentStatusDistributionDto(
    int PaidCount,
    decimal PaidPercentage,
    int PartialCount,
    decimal PartialPercentage,
    int UnpaidCount,
    decimal UnpaidPercentage
);
```

**SectionBreakdownDto:**
```csharp
public record SectionBreakdownDto(
    string SectionName,  // "Vegetable Area", "Fish Section", "Corner", etc.
    decimal Revenue,
    decimal Percentage
);
```

**TopStallDto:**
```csharp
public record TopStallDto(
    string StallNumber,
    string OccupantName,  // "Vacant" if no active contract
    decimal Revenue
);
```

**CollectionPerformanceDto:**
```csharp
public record CollectionPerformanceDto(
    int FullyPaidCount,
    int PartiallyPaidCount,
    int UnpaidCount
);
```

### Entity Relationships

The repository queries these entities:

```
Facility (1) ──────< (M) Stall
                         │
                         ├──< (M) Contract (for occupant names)
                         ├──< (M) PaymentRecord (monthly billing)
                         └──< (M) DailyCollection (NPM only)
```

**Key Entity Properties Used:**

**Stall:**
- `FacilityId`, `StallNo`, `Status`, `Section`, `AreaLocation`, `MonthlyRate`

**Contract:**
- `StallId`, `ActualOccupant`, `IsActive`

**PaymentRecord:**
- `StallId`, `BillingYear`, `BillingMonth`, `Status`
- `BaseRentalAmount`, `ElecAmount`, `WaterAmount`, `FishKilos`
- Computed: `TotalBill = BaseRentalAmount + (ElecAmount ?? 0) + (WaterAmount ?? 0) + (FishKilos * 1.0m ?? 0)`

**DailyCollection (NPM only):**
- `StallId`, `CollectionDate`, `IsPaid`, `DailyFee`, `FishKilos`
- Computed: `TotalCollected = DailyFee + (FishKilos * 1.0m ?? 0)` when `IsPaid = true`

# Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

## Property-Based Testing Applicability Assessment

This feature involves **complex data aggregation and reporting** with the following characteristics:
- Queries existing database records (read-only operations)
- Performs statistical calculations (sums, averages, percentages)
- Aggregates data across multiple time periods
- Handles two distinct facility types with different billing models

**Assessment:** Property-based testing is **partially applicable** for this feature. While the aggregation logic itself has universal properties, the feature is primarily a **read-only reporting endpoint** that depends heavily on existing database state. The most valuable testing approach combines:

1. **Property-based tests** for calculation logic (growth percentages, collection rates, trend aggregations)
2. **Example-based integration tests** for end-to-end query execution with known data sets
3. **Unit tests** for edge cases (zero revenue, missing previous period, vacant stalls)

Given the nature of this feature as a **complex reporting query**, we will focus on **example-based integration tests** and **unit tests for calculation logic** rather than pure property-based tests. The repository aggregations are best validated with known data scenarios.

**Decision:** Skip the Correctness Properties section. Use example-based integration tests and unit tests instead.

## Error Handling

### Validation Errors

**FluentValidation** handles input validation before the handler executes:

1. **Invalid facility code**: Returns `400 Bad Request` with validation error
2. **Invalid period**: Returns `400 Bad Request` with validation error
3. **Missing required parameters**:
   - Weekly period without month/weekNumber: `400 Bad Request`
   - Monthly period without month: `400 Bad Request`
4. **Invalid year range**: Returns `400 Bad Request` with validation error

### Business Logic Errors

1. **Facility not found**: Returns `404 Not Found`
2. **No data for period**: Returns empty collections with zero values (not an error)
3. **Previous period missing**: Growth percentage returns 0% (graceful degradation)

### Database Errors

- **Connection failures**: Caught by `ExceptionHandlingMiddleware`, returns `500 Internal Server Error`
- **Query timeouts**: Caught by middleware, returns `500 Internal Server Error`
- **Constraint violations**: Should not occur (read-only queries)

### Edge Cases

1. **Zero revenue in previous period**: Growth calculation returns 0% (avoid division by zero)
2. **No active stalls**: All metrics return 0, empty collections
3. **Vacant stalls**: Use "Vacant" as occupant name in TopStallDto
4. **Stalls without payment records**: Count as Unpaid in distribution
5. **NPM with no daily collections**: Include only monthly payment records

## Testing Strategy

### Unit Tests

**Focus:** Calculation logic and edge cases

**Test Cases:**
1. **Growth percentage calculation**:
   - Current > Previous: Positive growth
   - Current < Previous: Negative growth
   - Previous = 0: Returns 0% (not infinity)
   - Previous = null: Returns 0%

2. **Collection rate calculation**:
   - All paid: 100%
   - None paid: 0%
   - Mixed status: Correct percentage
   - No stalls: 0%

3. **Revenue aggregation**:
   - NPM: Daily collections + monthly payments
   - Other facilities: Monthly payments only
   - Fish fee calculation: FishKilos × ₱1.00
   - Utilities: ElecAmount + WaterAmount

4. **Payment status distribution**:
   - Stalls with no payment record: Count as Unpaid
   - Percentage calculation: (count / total) × 100
   - All statuses sum to 100%

5. **Section breakdown**:
   - NPM: VegetableArea, FishSection, MeatSection
   - NCC: Corner, Extension
   - Other facilities: Empty list
   - Percentages sum to 100%

### Integration Tests

**Focus:** End-to-end query execution with known data

**Test Scenarios:**

1. **Weekly report for NPM**:
   - Seed: 7 days of daily collections + monthly payments
   - Verify: 7 trend data points (Mon-Sun)
   - Verify: Revenue includes both daily and monthly
   - Verify: Section breakdown includes all 3 sections

2. **Monthly report for TCC**:
   - Seed: 6 months of payment records
   - Verify: 6 trend data points (last 6 months)
   - Verify: Collection rate calculation
   - Verify: Top 4 stalls by revenue

3. **Yearly report for NCC**:
   - Seed: 5 years of payment records
   - Verify: 5 trend data points (last 5 years)
   - Verify: Section breakdown (Corner vs Extension)
   - Verify: Growth percentage vs previous year

4. **Empty data scenarios**:
   - No payment records: Returns zeros, empty collections
   - No active stalls: Returns zeros
   - No previous period: Growth = 0%

5. **Mixed payment statuses**:
   - Seed: Paid, Partial, Unpaid records
   - Verify: Distribution percentages
   - Verify: Pending amount calculation
   - Verify: Collection performance counts

### Performance Tests

**Focus:** Query optimization and response time

**Metrics:**
- Response time < 500ms for typical data volume (100 stalls, 12 months)
- Response time < 2s for large data volume (500 stalls, 5 years)
- Database query count: Single query per aggregation (no N+1)

**Optimization Techniques:**
- `AsNoTracking()` for read-only queries
- `Include()` for necessary navigation properties only
- Database-side aggregations using LINQ
- Indexed columns: `FacilityId`, `BillingYear`, `BillingMonth`, `CollectionDate`

### Test Data Setup

**Seeding Strategy:**
1. Create facility with known FacilityCode
2. Create stalls with known StallNo and rates
3. Create contracts with known occupants
4. Create payment records with known amounts and statuses
5. Create daily collections (NPM only) with known dates

**Test Data Patterns:**
- Use fixed dates for predictable period calculations
- Use round numbers for easy verification (₱1000, ₱2000)
- Include edge cases (vacant stalls, zero amounts, missing data)

## Implementation Plan

### Phase 1: Core Infrastructure

1. **Create ReportPeriod enum** in Domain/Enums
2. **Create all DTOs** in Application/Dtos/Facilities
3. **Create repository interface** in Application/Common/Interface/Persistence
4. **Register repository** in Infrastructure/DependencyInjection.cs

### Phase 2: Query Implementation

1. **Create GetFacilityReportsQuery** record
2. **Create GetFacilityReportsQueryValidator** with FluentValidation rules
3. **Create GetFacilityReportsQueryHandler** with facility existence check

### Phase 3: Repository Implementation

1. **Create FacilityReportsRepository** class
2. **Implement summary metrics calculation**:
   - Total revenue (NPM: daily + monthly, others: monthly only)
   - Revenue growth (compare to previous period)
   - Collection rate (paid / total × 100)
   - Collection growth (compare to previous period)
   - Occupancy (active contracts count)
   - Pending payments (unpaid + partial)

3. **Implement revenue trend generation**:
   - Weekly: 7 data points (Mon-Sun)
   - Monthly: 6 data points (last 6 months)
   - Yearly: 5 data points (last 5 years)

4. **Implement payment distribution**:
   - Count by status (Paid, Partial, Unpaid)
   - Calculate percentages
   - Handle stalls without payment records

5. **Implement section breakdown**:
   - NPM: Group by MarketSection
   - NCC: Group by NccAreaLocation
   - Others: Return empty list

6. **Implement top stalls**:
   - Order by revenue descending
   - Take top 4
   - Include occupant name or "Vacant"

7. **Implement collection performance**:
   - Count fully paid, partially paid, unpaid

### Phase 4: API Endpoint

1. **Add endpoint to FacilitiesController**
2. **Add [Authorize] attribute**
3. **Map query parameters to GetFacilityReportsQuery**
4. **Return ActionResult<FacilityReportsDto>**

### Phase 5: Testing

1. **Write unit tests** for calculation logic
2. **Write integration tests** for end-to-end scenarios
3. **Write performance tests** for query optimization
4. **Manual testing** with Swagger UI

### Phase 6: Documentation

1. **Update API documentation** (Swagger comments)
2. **Update CURRENT_API_DOCUMENTATION.md**
3. **Create usage examples** for frontend integration

## Dependencies

### NuGet Packages (Already Installed)

- Microsoft.EntityFrameworkCore (9.0.4)
- Npgsql.EntityFrameworkCore.PostgreSQL (9.0.4)
- MediatR
- FluentValidation
- Microsoft.AspNetCore.Authentication.JwtBearer (9.0.5)

### Project References

- **Application** → Domain
- **Infrastructure** → Application + Domain
- **API** → Application + Infrastructure

### Database Schema

**Existing Tables (No Migrations Required):**
- Facilities
- Stalls
- Contracts
- PaymentRecords
- DailyCollections

**Indexes (Already Exist):**
- Stalls: (FacilityId, StallNo) unique
- PaymentRecords: (StallId, BillingYear, BillingMonth) unique
- DailyCollections: (StallId, CollectionDate) unique

## Performance Considerations

### Query Optimization

1. **Use AsNoTracking()**: Read-only queries don't need change tracking
2. **Use Select projections**: Fetch only required fields
3. **Use Include strategically**: Load navigation properties in single query
4. **Aggregate in database**: Use LINQ for database-side calculations
5. **Avoid N+1 queries**: Eager load related entities

### Caching Strategy (Future Enhancement)

- Cache facility reports for 5 minutes
- Invalidate cache on payment record changes
- Use distributed cache (Redis) for multi-instance deployments

### Database Indexes

**Existing Indexes (Sufficient):**
- `Stalls.FacilityId` (foreign key, auto-indexed)
- `PaymentRecords.StallId` (foreign key, auto-indexed)
- `PaymentRecords.(BillingYear, BillingMonth)` (part of unique constraint)
- `DailyCollections.StallId` (foreign key, auto-indexed)
- `DailyCollections.CollectionDate` (part of unique constraint)

**No Additional Indexes Required** - Existing indexes cover all WHERE clauses.

## Security Considerations

### Authentication

- **Endpoint requires [Authorize] attribute**
- **JWT token validation** via AuthorizationDelegatingHandler
- **Role-based access**: Admin and SuperAdmin only (enforced by auth policy)

### Authorization

- **No facility-level authorization**: All admins can view all facility reports
- **Collector access**: Not allowed (collectors only record payments, don't view reports)

### Data Privacy

- **No PII exposure**: Reports contain aggregated data only
- **Occupant names**: Shown in TopStallDto (business requirement)
- **OR numbers**: Not included in reports (audit trail only)

### Input Validation

- **FluentValidation**: Validates all query parameters
- **Enum validation**: Ensures valid FacilityCode and ReportPeriod
- **Range validation**: Year, month, weekNumber within valid ranges
- **SQL injection**: Not possible (EF Core parameterized queries)

## Deployment Considerations

### Configuration

**No new configuration required** - Uses existing:
- Database connection string (appsettings.json)
- JWT authentication settings (appsettings.json)
- CORS policy (Program.cs)

### Database Migrations

**No migrations required** - Uses existing schema.

### API Versioning

**Current version**: v1 (implicit)
**Endpoint**: `/api/facilities/{facilityCode}/reports`
**Future versioning**: Add `/api/v2/facilities/{facilityCode}/reports` if breaking changes needed

### Monitoring

**Logging:**
- Request/response logging via ExceptionHandlingMiddleware
- Query execution time logging (EF Core)
- Error logging with stack traces

**Metrics:**
- Response time per facility
- Query execution time
- Error rate by endpoint

### Rollback Plan

**If issues occur:**
1. Remove endpoint from FacilitiesController (comment out)
2. Redeploy API
3. Frontend falls back to mock data (already implemented)

**No database changes to rollback** - Read-only feature.

## Future Enhancements

### Phase 2 Features

1. **Export to PDF/Excel**: Generate downloadable reports
2. **Email reports**: Schedule automated report delivery
3. **Custom date ranges**: Allow arbitrary start/end dates
4. **Comparison mode**: Compare two periods side-by-side
5. **Forecasting**: Predict future revenue based on trends

### Performance Improvements

1. **Response caching**: Cache reports for 5 minutes
2. **Background processing**: Pre-generate reports for common periods
3. **Materialized views**: Store pre-aggregated data for faster queries

### Analytics Enhancements

1. **Delinquency trends**: Track payment delays over time
2. **Occupancy trends**: Track stall vacancy rates
3. **Revenue per square meter**: Normalize by stall size
4. **Collector performance**: Track collection efficiency by collector

## Conclusion

The Facility Reports endpoint provides comprehensive analytics for all six municipal facilities, supporting data-driven decision-making for facility management. The implementation follows Clean Architecture with CQRS pattern, ensuring maintainability and testability. The repository pattern handles complex aggregations efficiently, and the DTO structure provides a clear contract for frontend integration.

**Key Design Decisions:**
1. **Repository pattern for aggregations**: Keeps handler thin, testable
2. **DTOs for all responses**: Clear contract, no entity exposure
3. **Database-side aggregations**: Performance optimization
4. **Graceful degradation**: Returns zeros for missing data, not errors
5. **Dual facility type support**: NPM daily collections + monthly rentals

**Success Criteria:**
- ✅ All 15 requirements implemented
- ✅ Response time < 500ms for typical data
- ✅ Clean Architecture maintained
- ✅ Comprehensive test coverage
- ✅ Frontend integration ready
