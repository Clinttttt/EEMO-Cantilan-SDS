# Missing React Components - Implementation Plan

## Components Status

### ✅ Already Implemented
1. **Sidebar** - `src/components/layout/Sidebar.tsx`
2. **AddVendorModal** - `src/components/features/vendors/AddVendorModal.tsx`
3. **PaymentHistoryModal** - `src/components/features/vendors/PaymentHistoryModal.tsx`
4. **Modal** - `src/components/shared/Modal.tsx`
5. **Button** - `src/components/shared/Button.tsx`
6. **Spinner** - `src/components/shared/Spinner.tsx`

### ❌ Missing - Need to Create

#### Priority 1: Critical Components
1. **Profile Page** (View Details) - `src/pages/Profile.tsx`
   - Full stall profile page with all details
   - 2-column layout with cards
   - Edit modal integration
   - Payment recording integration
   - 12-month payment history visualization

2. **FacilityStallsTable** - `src/components/shared/FacilityStallsTable.tsx`
   - Generic table component for all facilities
   - Accepts generic TStall type
   - Action buttons: Payment, History, Profile
   - Row styling based on payment status

3. **FacilityPaymentModal** - `src/components/features/payments/FacilityPaymentModal.tsx`
   - Record payment modal
   - Payment status selection (Paid/Partial/Unpaid)
   - Partial amount input
   - Confirmation flow

#### Priority 2: Supporting Components
4. **ActionBar** - `src/components/shared/ActionBar.tsx`
   - Quick action buttons for facility pages
   - Facility-specific actions

5. **StallHoldersList** - `src/components/features/stalls/StallHoldersList.tsx`
   - List view of stall holders
   - Alternative to table view

#### Priority 3: Specialized Components
6. **SlaughterRecordModal** - `src/components/features/slaughterhouse/SlaughterRecordModal.tsx`
   - Slaughterhouse-specific recording
   - Animal type selection
   - Per-head charges

7. **Toolbar** - Complete `src/components/shared/Toolbar.tsx`
   - Search, filters, actions
   - Reusable across pages

## Implementation Order

### Phase 1: Complete View Details (Profile Page)
This is the most critical missing piece. The "View Details" button in Vendors page should open a full profile page.

**Files to create:**
1. `src/pages/Profile.tsx` - Main profile page
2. `src/styles/Profile.css` - Profile page styles (copy from Blazor)
3. Update `src/App.tsx` - Add profile route

### Phase 2: Generic Table Component
Create the reusable FacilityStallsTable component that can be used across all facility pages.

**Files to create:**
1. `src/components/shared/FacilityStallsTable.tsx`
2. `src/styles/FacilityStallsTable.css`

### Phase 3: Payment Modal
Create the facility payment recording modal.

**Files to create:**
1. `src/components/features/payments/FacilityPaymentModal.tsx`
2. `src/styles/FacilityPaymentModal.css`

### Phase 4: Supporting Components
Create remaining utility components as needed.

## Next Steps

1. Start with Profile page (most important for completing View Details functionality)
2. Copy Profile.razor.css to React
3. Create Profile.tsx following React patterns
4. Add route to App.tsx
5. Update Vendors.tsx to navigate to profile page instead of opening modal

## Notes

- All components must follow the global CSS pattern (no CSS Modules)
- Use the same class naming conventions as Blazor
- Maintain pixel-perfect visual consistency
- Follow React architecture rules from `.amazonq/rules/`
