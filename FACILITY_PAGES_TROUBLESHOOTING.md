# Facility Pages - Troubleshooting Guide

## Issue: Layout Broken / Content Not Displaying Properly

### Root Cause
The reusable `FacilityPage.razor` component was using CSS classes (`.tcc-hero`, `.tcc-section-info`, etc.) but didn't have its own CSS file. Each individual facility page (TCC.razor, BBQ.razor, etc.) had their own scoped CSS files with facility-specific class names.

### Solution Applied
Created `FacilityPage.razor.css` with all the necessary styles. Blazor's CSS isolation will automatically scope these styles to the FacilityPage component.

## Files Structure

```
Components/
├── Shared/
│   ├── FacilityPage.razor          ← Reusable component
│   └── FacilityPage.razor.css      ← Shared styles (NEW)
└── Pages/
    └── Menus/
        └── Facilities/
            ├── TCC.razor               ← Still using old structure
            ├── TCC.razor.css
            ├── NCC.razor               ← Using FacilityPage ✅
            ├── NCC.razor.css           ← Can be deleted
            ├── BBQ.razor               ← Using FacilityPage ✅
            ├── BBQ.razor.css           ← Can be deleted
            ├── ICEPLANT.razor          ← Using FacilityPage ✅
            ├── ICEPLANT.razor.css      ← Can be deleted
            ├── NPM.razor               ← Custom (daily collection)
            └── SH.razor                ← Custom (slaughterhouse)
```

## CSS Class Names Used

All classes use `.tcc-` prefix for consistency:
- `.tcc-hero` - Hero banner container
- `.tcc-hero-left`, `.tcc-hero-icon`, `.tcc-hero-title` - Hero banner elements
- `.tcc-hero-stats`, `.tcc-hero-stat`, `.tcc-hero-val`, `.tcc-hero-key` - Statistics
- `.tcc-hero-progress`, `.tcc-hero-progress-fill` - Progress bar
- `.tcc-section-info` - Info bar container
- `.tcc-section-label`, `.tcc-section-desc` - Info bar text
- `.tcc-section-mini-stats`, `.tcc-mini-stat`, `.tcc-mini-val`, `.tcc-mini-key` - Mini stats
- `.tcc-back-btn` - Back button

## How CSS Isolation Works

Blazor automatically:
1. Scopes CSS in `FacilityPage.razor.css` to `FacilityPage.razor` component
2. Adds unique attributes to elements (e.g., `b-xyz123`)
3. Rewrites CSS selectors to include these attributes
4. Result: Styles only apply to FacilityPage component instances

## If Layout Still Broken

### Step 1: Clear Browser Cache
- Hard refresh: `Ctrl + Shift + R` (Windows/Linux) or `Cmd + Shift + R` (Mac)
- Or clear browser cache completely

### Step 2: Rebuild Solution
```bash
dotnet clean
dotnet build
```

### Step 3: Check Browser DevTools
1. Open DevTools (F12)
2. Go to Elements/Inspector tab
3. Check if elements have the CSS classes
4. Check if `FacilityPage.razor.css` is loaded in Network tab
5. Look for any 404 errors for CSS files

### Step 4: Verify File Locations
Ensure these files exist:
- `Components/Shared/FacilityPage.razor`
- `Components/Shared/FacilityPage.razor.css`

### Step 5: Check for Typos
Verify class names in `FacilityPage.razor` match those in `FacilityPage.razor.css`

## Testing Each Facility

### NCC (New Commercial Center)
- URL: `/ncc` or `/facility/ncc`
- Should show: Hero banner, stats, stalls table
- Icon: Grid/building icon

### BBQ (Barbecue Stand)
- URL: `/bbq` or `/facility/bbq`
- Should show: Hero banner, stats, stalls table
- Icon: BBQ grill icon

### ICE (Iceplant)
- URL: `/ice` or `/facility/ice`
- Should show: Hero banner, stats, stalls table
- Icon: Star/snowflake icon

## Expected Behavior

1. **Hero Banner**: Navy blue background with gold accents, facility icon, name, and 4 statistics
2. **Progress Bar**: Gold gradient bar at bottom of hero showing collection rate
3. **Info Bar**: White card with facility description and mini stats (Paid, Unpaid, Partial, Collected)
4. **Toolbar**: Search box, filter tabs (All, Paid, Partial, Unpaid, Closed), Add New button
5. **Table**: Stalls list with payment status, OR numbers, and action buttons

## Common Issues

### Issue: "₱0 Collected, ₱0 Pending, 0% Collection Rate"
**Cause**: No stalls in database or payment data not loaded
**Solution**: Add stalls via "Add New" button or check API connection

### Issue: Table shows "No Stalls Found"
**Cause**: No stalls match current filter or search query
**Solution**: Clear search, change filter to "All", or add stalls

### Issue: Modals not opening
**Cause**: JavaScript not loaded or modal component not registered
**Solution**: Check browser console for errors, verify modal components exist

## Next Steps

If you want to update TCC.razor to also use the reusable component:
1. Replace TCC.razor content with the same pattern as NCC.razor
2. Change `FacilityCodeEnum="FacilityCode.TCC"`
3. Update icon, name, and description
4. Keep TCC.razor.css for now (won't hurt, just unused)

## Cleanup (Optional)

Once confirmed working, you can delete these unused CSS files:
- `NCC.razor.css`
- `BBQ.razor.css`
- `ICEPLANT.razor.css`

These are no longer needed since FacilityPage.razor.css provides all styles.
