# Reusable FacilityPage Component Implementation

## Summary
Created a reusable `FacilityPage.razor` component that encapsulates the common structure for facility pages (TCC, NCC, BBQ, ICE). This eliminates code duplication and ensures consistency across all facility pages.

## Files Created/Modified

### New Files
1. **`EEMOCantilanSDS.Client/Components/Shared/FacilityPage.razor`**
   - Generic reusable component for all monthly rental facilities
   - Handles: Hero banner, toolbar, stats, stalls table, payment modal, history modal, add vendor modal
   - Uses generic type parameter `TStall` for flexibility
   - Automatically loads stalls from API and manages payment status

### Modified Files
1. **`EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/NCC.razor`**
   - Reduced from ~400 lines to ~75 lines
   - Now uses `FacilityPage` component

2. **`EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/BBQ.razor`**
   - Reduced from ~350 lines to ~70 lines
   - Now uses `FacilityPage` component

3. **`EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/ICEPLANT.razor`**
   - Reduced from ~350 lines to ~70 lines
   - Now uses `FacilityPage` component

## Component Parameters

The `FacilityPage` component accepts the following parameters:

### Basic Configuration
- `FacilityCodeEnum` - Enum value (e.g., `FacilityCode.TCC`)
- `FacilityName` - Full name (e.g., "Tampak Commercial Center")
- `FacilityShortCode` - Short code (e.g., "TCC")
- `FacilityDescription` - Description shown in info bar
- `FacilityIcon` - RenderFragment for the facility icon SVG
- `DefaultMonthlyRate` - Default rate for new stalls
- `DefaultFeeTypes` - List of default fees (e.g., ["Electricity", "Water"])

### Selector Functions (for generic type support)
- `StallMapper` - Maps API DTO to stall model
- `IsPaidSelector` - Func to check if stall is paid
- `IsPartialSelector` - Func to check if stall has partial payment
- `IsActiveSelector` - Func to check if stall is active
- `MonthlyRateSelector` - Func to get monthly rate
- `PartialAmountSelector` - Func to get partial amount
- `StallNoSelector` - Func to get stall number
- `ActualOccupantSelector` - Func to get occupant name
- `ContractNameSelector` - Func to get contract name
- `ORNumberSetter` - Action to set OR number

## Usage Example

```razor
@page "/ncc"
@attribute [Authorize]
@using EEMOCantilanSDS.Domain.Enums
@rendermode InteractiveServer

<PageTitle>EEMO Admin — New Commercial Center</PageTitle>

<FacilityPage TStall="NccStall"
              FacilityCodeEnum="FacilityCode.NCC"
              FacilityName="New Commercial Center"
              FacilityShortCode="NCC"
              FacilityDescription="Monthly rental · 1,200.00 - 3,840.00 · Stall sizes: 10.5-70+ sqm"
              FacilityIcon="@NccIcon"
              DefaultMonthlyRate="1200"
              DefaultFeeTypes="@(new List<string>())"
              StallMapper="@MapStall"
              IsPaidSelector="@(s => s.IsPaid)"
              IsPartialSelector="@(s => s.IsPartial)"
              IsActiveSelector="@(s => s.IsActive)"
              MonthlyRateSelector="@(s => s.MonthlyRate)"
              PartialAmountSelector="@(s => s.PartialAmount)"
              StallNoSelector="@(s => s.StallNo)"
              ActualOccupantSelector="@(s => s.ActualOccupant)"
              ContractNameSelector="@(s => s.ContractName)"
              ORNumberSetter="@((s, or) => s.ORNumber = or)" />

@code {
    private RenderFragment NccIcon => @<svg viewBox="0 0 24 24">...</svg>;

    public class NccStall
    {
        public string StallNo { get; set; } = string.Empty;
        public string ActualOccupant { get; set; } = string.Empty;
        public string ContractName { get; set; } = string.Empty;
        public decimal MonthlyRate { get; set; }
        public bool IsPaid { get; set; }
        public bool IsPartial { get; set; }
        public decimal PartialAmount { get; set; }
        public bool IsActive { get; set; } = true;
        public string ORNumber { get; set; } = string.Empty;
        public Dictionary<string, bool> PaymentHistory { get; set; } = new();
    }

    private NccStall MapStall(Application.Dtos.Stalls.StallDto dto, bool isPaid, bool isPartial, decimal partialAmount, string orNumber)
    {
        return new NccStall
        {
            StallNo = dto.StallNo,
            ActualOccupant = dto.ActualOccupant ?? "N/A",
            ContractName = dto.NameOnContract ?? "N/A",
            MonthlyRate = dto.MonthlyRate,
            IsActive = dto.Status == StallStatus.Active,
            ORNumber = orNumber,
            IsPaid = isPaid,
            IsPartial = isPartial,
            PartialAmount = partialAmount,
            PaymentHistory = new()
        };
    }
}
```

## Features Included

### Automatic API Integration
- Loads stalls from API on initialization
- Fetches payment status for current month
- Handles pagination (up to 100 stalls)

### Statistics Display
- Total collected amount
- Total pending amount
- Collection rate percentage
- Total active stalls count
- Progress bar visualization

### Filtering & Search
- Filter by: All, Paid, Partial, Unpaid, Closed
- Search by stall number, occupant name, or contract name
- Real-time filtering

### Modals
- Payment recording modal (reuses `FacilityPaymentModal`)
- Payment history modal (reuses `PaymentHistoryModal`)
- Add vendor modal (reuses `AddVendorModal`)

### Toolbar Actions
- Search box
- Filter tabs
- Action bar (facility-specific quick actions)
- "Add New" button

## Benefits

1. **Code Reduction**: Reduced ~1,100 lines of duplicated code to ~300 lines of reusable component
2. **Consistency**: All facilities now have identical UI/UX
3. **Maintainability**: Changes to common functionality only need to be made once
4. **Type Safety**: Generic type parameter ensures compile-time type checking
5. **Flexibility**: Selector functions allow different stall models per facility

## Facilities Using This Component

- ✅ TCC (Tampak Commercial Center)
- ✅ NCC (New Commercial Center)
- ✅ BBQ (Barbecue Stand)
- ✅ ICE (Iceplant)

## Facilities NOT Using This Component

- ❌ NPM (New Public Market) - Has daily collection + sections, requires custom implementation
- ❌ SLH (Slaughterhouse) - Per-head transactions, completely different structure

## Next Steps

If you want to add a new facility:
1. Create a new `.razor` file in `Components/Pages/Menus/Facilities/`
2. Define your stall model class
3. Create a `MapStall` function
4. Use the `FacilityPage` component with appropriate parameters
5. Done! ~70 lines of code instead of ~400

## CSS Classes

The component reuses existing CSS classes:
- `.tcc-hero`, `.tcc-hero-*` - Hero banner styles
- `.tcc-section-info`, `.tcc-mini-stat` - Info bar styles
- All other styles from existing facility pages

Note: CSS class names use "tcc" prefix but work for all facilities due to shared styling.
