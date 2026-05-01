# Blazor vs React Components Analysis

## Screenshot Analysis

From the provided screenshots, I can see:
1. **Vendors & Stalls Page** - Main listing page with hero banner, facility selector, filters, search, and data table
2. **Profile Slide-in Panel** - Right-side panel showing vendor profile with monthly rental and action buttons

## Blazor Reusable Components (Shared folder)

### ✅ Already Migrated to React:
1. **Sidebar.razor** → `src/components/layout/Sidebar.tsx` ✅
2. **AddVendorModal.razor** → `src/components/features/vendors/AddVendorModal.tsx` ✅
3. **PaymentHistoryModal.razor** → `src/components/features/vendors/PaymentHistoryModal.tsx` ✅
4. **Profile.razor** → `src/pages/Profile.tsx` ✅ (as full page, not slide-in)
5. **Toolbar.razor** → `src/components/shared/Toolbar.tsx` ✅ (partial)
6. **ActionBar.razor** → `src/components/shared/ActionBar.tsx` ✅ (exists but needs review)

### ❌ Missing in React (Need to Create):
1. **FacilityStallsTable.razor** - Generic reusable table component with TStall generic type
2. **FacilityPaymentModal.razor** - Payment recording modal (different from PaymentHistoryModal)
3. **StallHoldersList.razor** - List view of stall holders
4. **SlaughterRecordModal.razor** - Slaughterhouse-specific recording modal

## Component-by-Component Status

### 1. FacilityStallsTable.razor
**Status:** ❌ Missing
**Purpose:** Generic reusable table for displaying stalls across all facilities
**Features:**
- Generic `@typeparam TStall` for flexibility
- Columns: Stall No, Actual Occupant, OR No, Section/Area, Monthly Rate, Status, Actions
- Action buttons: View Details, View Profile, Edit, Payment History, Close/Reopen
**React Equivalent:** Need to create `src/components/shared/FacilityStallsTable.tsx`

### 2. FacilityPaymentModal.razor
**Status:** ❌ Missing
**Purpose:** Record payment for a stall (different from viewing payment history)
**Features:**
- Payment amount input
- OR Number input
- Payment status selection (Paid/Partial)
- Utilities amount
- Fish kilos (for NPM Fish section)
**React Equivalent:** Need to create `src/components/features/payments/FacilityPaymentModal.tsx`

### 3. StallHoldersList.razor
**Status:** ❌ Missing
**Purpose:** Alternative list view of stall holders (simpler than table)
**Features:**
- Card-based layout
- Quick view of stall holders
- Compact display
**React Equivalent:** Need to create `src/components/features/stalls/StallHoldersList.tsx`

### 4. SlaughterRecordModal.razor
**Status:** ❌ Missing
**Purpose:** Record slaughterhouse transactions
**Features:**
- Animal type selection (Hog, Carabao, Cow)
- Head count input
- Fee calculation
- OR Number
**React Equivalent:** Need to create `src/components/features/slaughterhouse/SlaughterRecordModal.tsx`

### 5. Toolbar.razor
**Status:** ⚠️ Partial (needs enhancement)
**Current:** Basic toolbar exists
**Missing:**
- Facility selector dropdown
- Status filter tabs
- Search box
- Action buttons
**Note:** Vendors.tsx has inline toolbar implementation. Need to extract to reusable component.

### 6. ActionBar.razor
**Status:** ⚠️ Exists but needs review
**Purpose:** Facility-specific quick action buttons
**Features:**
- Quick payment recording
- Quick stall creation
- Facility-specific actions
**React Equivalent:** `src/components/shared/ActionBar.tsx` exists but may need enhancement

## Priority Order for Creation

### High Priority (Core Functionality):
1. **FacilityPaymentModal** - Critical for payment recording workflow
2. **FacilityStallsTable** - Reusable table component for all facility pages

### Medium Priority (Enhanced UX):
3. **SlaughterRecordModal** - Specific to SLH facility
4. **StallHoldersList** - Alternative view option

### Low Priority (Nice to Have):
5. **Enhanced Toolbar** - Extract from Vendors.tsx to reusable component
6. **Enhanced ActionBar** - Review and enhance existing component

## Implementation Plan

### Phase 1: Core Payment Workflow
- [ ] Create FacilityPaymentModal.tsx
- [ ] Copy FacilityPaymentModal.razor.css to src/styles/
- [ ] Add CSS import to main.tsx
- [ ] Test payment recording flow

### Phase 2: Reusable Table Component
- [ ] Create FacilityStallsTable.tsx with TypeScript generics
- [ ] Copy FacilityStallsTable.razor.css to src/styles/
- [ ] Add CSS import to main.tsx
- [ ] Refactor Vendors.tsx to use FacilityStallsTable

### Phase 3: Facility-Specific Components
- [ ] Create SlaughterRecordModal.tsx
- [ ] Copy SlaughterRecordModal.razor.css to src/styles/
- [ ] Add CSS import to main.tsx
- [ ] Create slaughterhouse page using the modal

### Phase 4: Alternative Views
- [ ] Create StallHoldersList.tsx
- [ ] Copy StallHoldersList.razor.css to src/styles/
- [ ] Add CSS import to main.tsx
- [ ] Add toggle between table and list views

### Phase 5: Component Enhancement
- [ ] Extract Toolbar from Vendors.tsx to reusable component
- [ ] Review and enhance ActionBar.tsx
- [ ] Add facility-specific action buttons

## Notes

- All components should use global CSS classes (not CSS Modules)
- Follow naming convention: component-specific prefix (e.g., `fpm-*` for FacilityPaymentModal)
- Maintain pixel-perfect consistency with Blazor design
- Use TypeScript generics where Blazor uses `@typeparam`
- All modals should use the `eemo-modal` structure from global.css

## Next Steps

1. Start with FacilityPaymentModal (highest priority)
2. Create one component at a time
3. Test each component thoroughly before moving to next
4. Update this document as components are completed
