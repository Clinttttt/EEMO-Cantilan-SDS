# EEMO Cantilan SDS — Blazor Server Quick Reference

**Location:** `EEMOCantilanSDS.Client/` (Blazor Server + .NET 9)

## Purpose
Blazor Server frontend for the EEMO Revenue Collection System. Communicates with ASP.NET Core Web API backend.

---

## Tech Stack

- **Framework:** Blazor Server (.NET 9)
- **Language:** C# 13
- **Rendering:** Server-side with SignalR
- **HTTP Client:** Typed API Clients (HttpClient wrapper)
- **Styling:** Custom CSS + Component-scoped CSS
- **Auth:** AuthenticationStateProvider + JWT Cookies

---

## Full Documentation

- **Architecture Rules:** `.amazonq/rules/arch-rules.md`
- **Patterns Reference:** `.amazonq/rules/patterns.md`
- **Domain Reference:** `.amazonq/rules/domain.md`
- **API Documentation:** `CURRENT_API_DOCUMENTATION.md`
- **Entity Documentation:** `CURRENT_ENTITY_DOCUMENTATION.md`
- **Architecture Documentation:** `ARCHITECTURE_DOCUMENTATION.md`

---

## Design Tokens (CSS Variables)

```css
/* app.css */
:root {
  --navy: #0d2137;
  --navy-2: #112d47;
  --navy-3: #1e3a5f;
  --gold: #c8a84b;
  --gold-light: #e8cc76;
  --green: #2d7a5f;
  --green-bg: #e6f4ef;
  --red: #8b3a3a;
  --red-bg: #fdf0f0;
  --bg: #f0f4f8;
  --bg-card: #ffffff;
  --bg-icon: #eef2f6;
  --border: #dde4ea;
  --text: #0d2137;
  --text-muted: #8faabf;
  --text-subtle: #6a8aa0;
}
```

---

## Common Components

### Layout
- `Sidebar.razor` — Navigation sidebar
- `AdminLayout.razor` — Main app shell with sidebar + topbar

### Shared
- `Toolbar.razor` — Search + filters + action buttons
- `ActionBar.razor` — Facility-specific quick actions

### Feature Components
- `FacilityStallsTable.razor` — Generic stall table
- `FacilityPaymentModal.razor` — Record payment modal
- `PaymentHistoryModal.razor` — 12-month payment ledger
- `PaymentConfirmationModal.razor` — Confirm payment before saving
- `Profile.razor` — Stall profile page

---

## Quick Patterns

### API Client Interface
```csharp
// Application/Common/Interface/ApiClients/IStallsApiClient.cs
public interface IStallsApiClient
{
    Task<Result<IReadOnlyList<StallDto>>> GetStallsAsync();
    Task<Result<StallDto>> CreateStallAsync(CreateStallCommand command);
}
```

### API Client Implementation
```csharp
// Infrastructure/HttpClients/ApiClients/StallsApiClient.cs
public class StallsApiClient(HttpClient http) : HandleResponse, IStallsApiClient
{
    public async Task<Result<IReadOnlyList<StallDto>>> GetStallsAsync()
        => await GetAsync<IReadOnlyList<StallDto>>("/api/stalls");
    
    public async Task<Result<StallDto>> CreateStallAsync(CreateStallCommand command)
        => await PostAsync<CreateStallCommand, StallDto>("/api/stalls", command);
}
```

### Page Component
```razor
@page "/stalls"
@inject IStallsApiClient StallsApi
@rendermode InteractiveServer

<PageTitle>Stalls</PageTitle>

@if (isLoading)
{
    <div class="spinner">Loading...</div>
}
else if (stalls != null)
{
    <div class="stalls-container">
        <h1>Stalls</h1>
        <StallTable Stalls="@stalls" />
    </div>
}

@code {
    private List<StallDto>? stalls;
    private bool isLoading = true;
    
    protected override async Task OnInitializedAsync()
    {
        var result = await StallsApi.GetStallsAsync();
        if (result.IsSuccess)
        {
            stalls = result.Value?.ToList();
        }
        isLoading = false;
    }
}
```

### Component with Parameters
```razor
@* StallTable.razor *@
<table class="stall-table">
    <thead>
        <tr>
            <th>Stall No</th>
            <th>Occupant</th>
            <th>Status</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var stall in Stalls)
        {
            <tr>
                <td>@stall.StallNo</td>
                <td>@stall.ActualOccupant</td>
                <td>@stall.Status</td>
            </tr>
        }
    </tbody>
</table>

@code {
    [Parameter]
    public IReadOnlyList<StallDto> Stalls { get; set; } = Array.Empty<StallDto>();
}
```

---

## Guidelines

### What Goes Where

**API Client Interfaces** (`Application/Common/Interface/ApiClients/`)
- One interface per feature area
- Methods return `Task<Result<T>>`
- Defined in Application layer

**API Client Implementations** (`Infrastructure/HttpClients/ApiClients/`)
- Implement interface
- Extend `HandleResponse` base class
- Use `GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync` methods
- Implemented in Infrastructure layer

**Page Components** (`Components/Pages/`)
- One per route
- Use `@page` directive
- Inject API clients via `@inject`
- Handle loading/error states
- Use `@rendermode InteractiveServer`

**Feature Components** (`Components/Pages/Shared/` or `Components/Modals/`)
- Domain-specific components
- Receive data via `[Parameter]`
- Emit events via `EventCallback`

**Shared Components** (`Components/Shared/`)
- Generic, reusable UI components
- No domain knowledge
- Fully controlled via parameters

---

## Styling Rules

- ✅ Use custom CSS classes
- ✅ Use CSS variables from `app.css`
- ✅ Component-scoped CSS via `.razor.css` files
- ✅ Ensure responsive design
- ✅ Add hover/focus states to interactive elements
- ❌ No inline styles
- ❌ No Tailwind CSS
- ❌ No CSS-in-JS libraries

---

## Auth Flow

1. User logs in → access token stored in cookie (15min)
2. Refresh token stored in httpOnly cookie (7 days)
3. `AuthorizationDelegatingHandler` adds access token to requests
4. On 401 → `RefreshTokenDelegatingHandler` auto refreshes → retry original request
5. On refresh failure → clear auth → redirect to login
6. `AuthenticationStateProvider` manages auth state

---

## Error Handling

- API clients return `Result<T>` (never throw)
- Display errors using `result.Error` from API call
- Backend validation errors: `result.Errors` (Dictionary<string, string[]>)
- Show first error per field: `errors[fieldName]?.FirstOrDefault()`

---

## Component Rules

- Use `@rendermode InteractiveServer` for interactive components
- Use `@inject` for dependency injection
- Use `[Parameter]` for component inputs
- Use `EventCallback` for component outputs
- Use `@code` blocks for component logic
- Use `protected override async Task OnInitializedAsync()` for data loading
- Use `StateHasChanged()` to trigger re-render

---

## Constants Matching Backend

```csharp
// Domain/Constants/FeeRates.cs (shared with backend)
public static class FeeRates
{
    public const decimal NpmDailyFee = 30.00m;
    public const decimal NpmFishFeePerKilo = 1.00m;
    public const decimal TccMonthlyMin = 2400.00m;
    // ... (all constants defined in backend)
}

public static class DomainRules
{
    public const int PaymentHistoryMonths = 12;
    public const int DelinquentThresholdMonths = 3;
    public const int ExpiringSoonMonths = 3;
}
```

---

## DON'Ts

- ❌ Store access token in localStorage
- ❌ Inject HttpClient directly (use typed API clients)
- ❌ Use inline styles
- ❌ Put business logic in components
- ❌ Use `<form>` tags (use `@onclick`/`@onchange`)
- ❌ Check HTTP status codes (use `Result<T>.IsSuccess`)
- ❌ Display all validation errors per field (show first only)
- ❌ Inject DbContext in components

---

## DOs

- ✅ Use typed API clients for all data fetching
- ✅ Use `Result<T>` pattern for error handling
- ✅ Use component-scoped CSS
- ✅ Handle loading/error states
- ✅ Use `@rendermode InteractiveServer`
- ✅ Follow component patterns
- ✅ Ensure accessibility
- ✅ Use `EventCallback` for parent-child communication
