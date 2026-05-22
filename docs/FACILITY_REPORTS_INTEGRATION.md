# Facility Reports Modal - Integration Guide

## Overview

The `FacilityReportsModal` component provides comprehensive analytics and reporting for facility pages (NPM, TCC, NCC, BBQ, ICE). It displays:

- Weekly/Monthly/Yearly revenue trends
- Collection rate statistics
- Payment status distribution
- Section-wise breakdown (for NPM)
- Top performing stalls
- Interactive charts and graphs

## Component Location

```
Components/Modals/FacilityReportsModal.razor
Components/Modals/FacilityReportsModal.razor.css
```

## Integration Steps

### 1. Add the Modal to Your Facility Page

Add the modal component at the bottom of your facility page (e.g., NPM.razor), after other modals:

```razor
<!-- ══════════════════════════════════════════════
     FACILITY REPORTS MODAL
══════════════════════════════════════════════ -->
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="NPM"
                      FacilityName="New Public Market"
                      ShowSectionBreakdown="true" />
```

### 2. Add State Variable in @code Block

```csharp
@code {
    // ... existing variables ...
    
    private bool ShowReports = false;
    
    // ... rest of code ...
}
```

### 3. Update InitializeToolbarActions Method

Add the "Reports" button to the toolbar actions:

```csharp
private void InitializeToolbarActions()
{
    ToolbarActions = new()
    {
        new()
        {
            Label = "Reports",
            Icon = """<svg viewBox="0 0 24 24"><path d="M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z" /></svg>""",
            ClassName = "btn-outline",
            Title = "View Facility Reports",
            OnClickHandler = async () => { ShowReports = true; await Task.CompletedTask; }
        },
        new()
        {
            Label = "Add New",
            Icon = """<svg viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>""",
            ClassName = "btn-primary",
            Title = "Add New Vendor",
            OnClickHandler = async () => { OpenAddVendorModal(); await Task.CompletedTask; }
        }
    };
}
```

## Complete Example for NPM.razor

### Step-by-Step Integration

#### 1. Add Modal Declaration (After existing modals, before @code)

```razor
<!-- ══════════════════════════════════════════════
     FACILITY REPORTS MODAL
══════════════════════════════════════════════ -->
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="NPM"
                      FacilityName="New Public Market"
                      ShowSectionBreakdown="true" />
```

#### 2. Add State Variable

```csharp
@code {
    // Existing variables
    bool SidebarCollapsed = false;
    string SearchQuery = string.Empty;
    string ActiveSection = "Vegetable Section";
    string ActiveStatus = "All";
    
    // ADD THIS:
    private bool ShowReports = false;
    
    // ... rest of existing code ...
}
```

#### 3. Update Toolbar Actions

```csharp
private void InitializeToolbarActions()
{
    ToolbarActions = new()
    {
        // ADD THIS FIRST:
        new()
        {
            Label = "Reports",
            Icon = """<svg viewBox="0 0 24 24"><path d="M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z" /></svg>""",
            ClassName = "btn-outline",
            Title = "View Facility Reports & Analytics",
            OnClickHandler = async () => { ShowReports = true; await Task.CompletedTask; }
        },
        // Existing Add New button:
        new()
        {
            Label = "Add New",
            Icon = """<svg viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>""",
            ClassName = "btn-primary",
            Title = "Add New Vendor",
            OnClickHandler = async () => { OpenAddVendorModal(); await Task.CompletedTask; }
        }
    };
}
```

## Integration for Other Facilities

### TCC (Tampak Commercial Center)

```razor
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="TCC"
                      FacilityName="Tampak Commercial Center"
                      ShowSectionBreakdown="false" />
```

### NCC (New Commercial Center)

```razor
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="NCC"
                      FacilityName="New Commercial Center"
                      ShowSectionBreakdown="true" />
```

### BBQ (Barbecue Stand)

```razor
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="BBQ"
                      FacilityName="Barbecue Stand"
                      ShowSectionBreakdown="false" />
```

### ICE (Iceplant)

```razor
<FacilityReportsModal IsOpen="@ShowReports"
                      IsOpenChanged="@((value) => ShowReports = value)"
                      FacilityCode="ICE"
                      FacilityName="Iceplant"
                      ShowSectionBreakdown="false" />
```

## Component Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `IsOpen` | `bool` | Yes | Controls modal visibility |
| `IsOpenChanged` | `EventCallback<bool>` | Yes | Two-way binding for modal state |
| `FacilityCode` | `string` | Yes | Facility code (NPM, TCC, NCC, BBQ, ICE) |
| `FacilityName` | `string` | Yes | Display name of the facility |
| `ShowSectionBreakdown` | `bool` | No | Show section-wise revenue breakdown (default: false) |

## Features

### 1. Period Selection
- **Weekly**: Shows 7-day data with daily breakdown
- **Monthly**: Shows 6-month trend
- **Yearly**: Shows 5-year historical data

### 2. Summary Cards
- Total Revenue with growth percentage
- Collection Rate with trend
- Occupancy statistics
- Pending payments count and amount

### 3. Charts
- **Revenue Trend Chart**: Bar chart showing revenue over time
- **Payment Status Distribution**: Donut chart showing Paid/Partial/Unpaid breakdown

### 4. Detailed Breakdown
- Revenue by Section (if `ShowSectionBreakdown="true"`)
- Collection Performance (Paid/Partial/Unpaid counts)
- Top Revenue Stalls

### 5. Export Options
- Export to PDF (placeholder for API implementation)
- Print Report (placeholder for API implementation)

## Mock Data

Currently, the component uses mock data for demonstration. The following properties contain sample data:

- `MockTotalRevenue`: ₱125,450.00
- `MockCollectionRate`: 87%
- `MockOccupiedStalls`: 28/32
- `MockPendingCount`: 4 stalls
- Chart data arrays for different periods

## Future API Integration

When implementing the backend API, replace mock data with actual API calls:

```csharp
protected override async Task OnParametersSetAsync()
{
    if (IsOpen)
    {
        await LoadReportData();
    }
}

private async Task LoadReportData()
{
    // TODO: Call API to get report data
    // var result = await ReportsApi.GetFacilityReportAsync(
    //     FacilityCode, 
    //     SelectedPeriod, 
    //     CurrentDate
    // );
    
    // if (result.IsSuccess)
    // {
    //     // Update component state with real data
    // }
}
```

## Styling

The modal uses the existing design system:
- CSS variables from `app.css` (--navy, --gold, --green, etc.)
- Consistent with other modals in the application
- Fully responsive design
- Smooth animations and transitions

## Notes

- The modal is excluded from Slaughterhouse, Tabuan, and Transport Terminal as per requirements
- Section breakdown is only shown for facilities with multiple sections (NPM, NCC)
- All monetary values are formatted with Philippine Peso (₱) symbol
- Dates use the current system date for demonstration

## Testing Checklist

- [ ] Modal opens when "Reports" button is clicked
- [ ] Period tabs (Weekly/Monthly/Yearly) switch correctly
- [ ] Date navigation (Previous/Next) works
- [ ] Charts render properly
- [ ] Summary cards display correct mock data
- [ ] Breakdown sections show appropriate data
- [ ] Modal closes properly
- [ ] Responsive design works on mobile
- [ ] Export/Print buttons are present (functionality pending)
