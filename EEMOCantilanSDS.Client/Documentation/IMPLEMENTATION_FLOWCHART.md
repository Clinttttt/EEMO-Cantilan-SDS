# Visual Implementation Guide - FacilityRecordModal

## Before vs After

### BEFORE (Old Pattern)
```
SH.razor (Slaughterhouse page)
├── Button: "Record Slaughter"
├── Modal code: Inline (repetitive)
├── No reusability
└── Difficult to maintain
```

### AFTER (New Pattern) ✅
```
Shared/FacilityRecordModal.razor (Reusable component)
│
├─→ NPM.razor (New Public Market)
├─→ TCC.razor (Tampak Commercial Center)
├─→ NCC.razor (New Commercial Center)
├─→ BBQ.razor (BBQ Stand)
├─→ ICEPLANT.razor (Iceplant)
└─→ SH.razor (Slaughterhouse) ✅ DONE
```

---

## Component Flow Diagram

```
Facility Page (e.g., NPM.razor)
│
├─ Button click
│  └─ OpenRecordModal()
│     └─ ShowModal = true
│
├─ Modal renders (ShowModal = true)
│  └─ FacilityRecordModal component
│     ├─ Header
│     │  ├─ @ModalTitle
│     │  ├─ @ModalSubtitle
│     │  └─ Close button → OnClose callback
│     │
│     ├─ Body (ChildContent)
│     │  ├─ Your form fields (facility-specific)
│     │  │  ├─ Date picker
│     │  │  ├─ Vendor/Tenant name
│     │  │  ├─ Amount
│     │  │  ├─ Type dropdown
│     │  │  └─ Other fields
│     │  └─ Error message (if any)
│     │
│     └─ Footer
│        ├─ Cancel button → OnClose callback
│        └─ Submit button → OnSubmit callback
│           ├─ Validation in code
│           ├─ Add entry to list
│           ├─ Close modal
│           └─ Reset form
```

---

## Code Structure for Each Facility

```csharp
@code {
    // 1. STATE VARIABLES
    private bool ShowRecordModal { get; set; }
    private string ErrorMessage { get; set; } = string.Empty;
    
    // 2. FORM CLASS
    private YourRecordForm RecordForm = new();
    
    // 3. OPEN MODAL
    private void OpenRecordModal()
    {
        RecordForm = new();
        ErrorMessage = string.Empty;
        ShowRecordModal = true;
    }
    
    // 4. CLOSE MODAL
    private async Task CloseRecordModal()
    {
        ShowRecordModal = false;
        RecordForm = new();
        ErrorMessage = string.Empty;
    }
    
    // 5. SUBMIT & VALIDATE
    private async Task SubmitRecord()
    {
        // Validation checks
        if (invalid) { ErrorMessage = "..."; return; }
        
        // Create entry
        var newEntry = new YourEntry(...);
        Entries.Add(newEntry);
        
        // Close & reset
        await CloseRecordModal();
    }
    
    // 6. FORM MODEL CLASS
    public class YourRecordForm
    {
        public DateTime Date { get; set; } = DateTime.Now;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        // ... add your properties
    }
}
```

---

## HTML Structure (What You Add)

```html
<!-- BUTTON (in toolbar) -->
<button class="btn-primary" @onclick="OpenRecordModal">
    <svg viewBox="0 0 24 24"><!-- Plus icon --></svg>
    Record [Activity]
</button>

<!-- MODAL (component usage) -->
<FacilityRecordModal 
    ShowModal="ShowRecordModal" 
    OnClose="CloseRecordModal"
    OnSubmit="SubmitRecord"
    ModalTitle="Record [Activity]"
    ModalSubtitle="Enter details..."
    SubmitButtonText="Add Record"
    ErrorMessage="@ErrorMessage">
    
    <!-- YOUR FORM FIELDS HERE -->
    <div class="form-section-divider">
        <div class="form-section-label">Section Title</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Field <span class="req">*</span></label>
                <input class="form-input" @bind="RecordForm.Property" />
            </div>
        </div>
    </div>

</FacilityRecordModal>
```

---

## State Flow (Timeline of Actions)

```
1. USER ACTION
   └─ Clicks "+ Record [Activity]" button
   
2. OPEN MODAL
   └─ @onclick="OpenRecordModal()"
      ├─ Create blank form
      ├─ Clear error message
      └─ Set ShowRecordModal = true
   
3. MODAL RENDERS
   └─ FacilityRecordModal component displays
      ├─ User sees form fields
      ├─ User enters data
      └─ User clicks Submit button
   
4. VALIDATE
   └─ OnSubmit="SubmitRecord()" fires
      ├─ Check required fields
      ├─ Check data types
      └─ If invalid:
         ├─ Set ErrorMessage
         ├─ Stop execution
         └─ User sees error in modal
   
5. CREATE ENTRY
   └─ If validation passes:
      ├─ Create new entry object
      ├─ Add to Entries list
      └─ Proceed to close
   
6. CLOSE & RESET
   └─ @onclick="CloseRecordModal()"
      ├─ Set ShowRecordModal = false
      ├─ Clear form
      ├─ Clear error message
      └─ Modal hidden
   
7. RESULT
   └─ Page refreshes (StateHasChanged)
      └─ New entry visible in table
```

---

## Form Field Patterns

### Pattern 1: Two-Column Layout
```razor
<div class="form-row-2">
    <div class="form-group">
        <label class="form-label">Left Field <span class="req">*</span></label>
        <input class="form-input" @bind="Form.LeftProperty" />
    </div>
    <div class="form-group">
        <label class="form-label">Right Field <span class="req">*</span></label>
        <input class="form-input" @bind="Form.RightProperty" />
    </div>
</div>
```

### Pattern 2: Full-Width Field
```razor
<div class="form-row-full">
    <div class="form-group">
        <label class="form-label">Full Width <span class="req">*</span></label>
        <textarea class="form-textarea" @bind="Form.TextArea"></textarea>
    </div>
</div>
```

### Pattern 3: Dropdown Select
```razor
<div class="form-group">
    <label class="form-label">Type <span class="req">*</span></label>
    <select class="form-input" @bind="Form.SelectedType">
        <option value="">-- Select Type --</option>
        <option value="Type1">Type 1</option>
        <option value="Type2">Type 2</option>
    </select>
</div>
```

### Pattern 4: Date Input
```razor
<div class="form-group">
    <label class="form-label">Date <span class="req">*</span></label>
    <input type="date" class="form-input" @bind="Form.Date" />
</div>
```

### Pattern 5: Number Input
```razor
<div class="form-group">
    <label class="form-label">Quantity <span class="req">*</span></label>
    <input type="number" class="form-input" min="0" placeholder="0" @bind="Form.Quantity" />
</div>
```

---

## Styling Applied Automatically

When you use the CSS classes, you get:
- ✅ Proper spacing and alignment
- ✅ Hover and focus states
- ✅ Error styling
- ✅ Required field indicators (red asterisk)
- ✅ Responsive layout
- ✅ Consistent colors
- ✅ Smooth transitions

---

## CSS Grid System

```
.form-row-2        → 2 columns (responsive)
.form-row-full     → 1 column (full width)

Small devices (< 768px) → All become 1 column
Large devices (≥ 768px) → Maintain grid layout
```

---

## Error Handling Flow

```
User submits form with invalid data:

1. OnSubmit fires
2. Validation check fails
3. Set ErrorMessage = "descriptive text"
4. Return (stop execution)
5. Modal stays open
6. Red error box appears below form
7. User can edit and resubmit

User submits valid form:

1. OnSubmit fires
2. All validations pass
3. Create entry
4. Call CloseRecordModal()
5. Modal disappears
6. New entry visible in table
```

---

## Implementation Checklist

For each facility, follow this:

```
☐ Read FACILITY_MODAL_TEMPLATES.md for your facility
☐ Copy the button code to your toolbar
☐ Copy the modal component code
☐ Update @ModalTitle, @ModalSubtitle, @SubmitButtonText
☐ Update form field bindings to your form class
☐ Create your RecordForm class in @code
☐ Implement validation logic in SubmitRecord()
☐ Create your Entry record type
☐ Test: Add an entry and verify in table
☐ Test: Try invalid data and see error
```

---

## Reusable Parts Across All Facilities

These CSS classes work everywhere:
```css
.form-section-divider     /* Section container */
.form-section-label       /* "SECTION NAME" text */
.form-row-2               /* Two-column grid */
.form-row-full            /* Full width */
.form-group               /* Field wrapper */
.form-label               /* Label text */
.form-input               /* Text/number/date input */
.form-select              /* Dropdown */
.form-textarea            /* Multi-line text */
.form-error               /* Error message box */
.btn-primary              /* Blue submit button */
.btn-ghost                /* Gray cancel button */
.req                      /* Red asterisk for required */
```

---

## Next: Test SH.razor

1. Visit `http://localhost:7167/facility/slh`
2. Click "+ Record Slaughter" button
3. Try adding a record with:
   - Date: Today
   - Owner: "Test Owner"
   - Animal: "Hog"
   - Heads: 3
   - OR: "TEST-001"
4. Click "Add Transaction"
5. Verify the new row appears in the table
6. Try submitting with invalid data (see error)

---

## Summary

✅ One reusable component
✅ Works for all facilities
✅ Customizable per facility
✅ Beautiful, consistent UI
✅ Full validation
✅ Easy to implement
✅ Well documented

Ready to apply to your other facilities! 🚀
