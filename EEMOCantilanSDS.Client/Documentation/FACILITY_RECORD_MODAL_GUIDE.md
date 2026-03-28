# FacilityRecordModal - Implementation Guide

## Overview
`FacilityRecordModal.razor` is a reusable, facility-specific modal component that handles adding records for each facility. It's based on the `Vendor.razor` modal design but tailored for facility-specific transactions.

## Features
- ✅ Reusable across all facilities (SH, NPM, TCC, NCC, BBQ, ICEPLANT)
- ✅ Customizable titles, subtitles, and submit button text
- ✅ Built-in error handling and validation messaging
- ✅ Form styling consistent with Vendor modal
- ✅ Responsive design
- ✅ Child content slots for facility-specific fields

## How to Use

### 1. **In Your Facility Page** (e.g., `SH.razor`, `NPM.razor`)

```razor
<!-- Add this button in your toolbar -->
<button class="btn-primary" @onclick="OpenRecordModal">
    <svg viewBox="0 0 24 24">...</svg>
    Record [Facility Type]
</button>

<!-- Add the modal component -->
<FacilityRecordModal 
    ShowModal="ShowRecordModal" 
    OnClose="CloseRecordModal"
    OnSubmit="SubmitRecord"
    ModalTitle="Record [Activity]"
    ModalSubtitle="Enter the details below."
    SubmitButtonText="Add Record"
    ErrorMessage="@ErrorMessage">
    
    <!-- Add your facility-specific form fields here -->
    <div class="form-section-divider">
        <div class="form-section-label">Your Section</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Field Name <span class="req">*</span></label>
                <input type="text" class="form-input" @bind="RecordForm.PropertyName" />
            </div>
        </div>
    </div>

</FacilityRecordModal>
```

### 2. **In Your Code Section** (`@code`)

```csharp
// State
private bool ShowRecordModal { get; set; }
private string ErrorMessage { get; set; } = string.Empty;

// Form model (create a class for your facility type)
private YourFacilityRecordForm RecordForm = new();

// Open modal
private void OpenRecordModal()
{
    RecordForm = new();
    ErrorMessage = string.Empty;
    ShowRecordModal = true;
}

// Close modal
private async Task CloseRecordModal()
{
    ShowRecordModal = false;
    RecordForm = new();
    ErrorMessage = string.Empty;
}

// Handle submission
private async Task SubmitRecord()
{
    // Validation
    if (string.IsNullOrWhiteSpace(RecordForm.SomeField))
    {
        ErrorMessage = "Some field is required.";
        return;
    }

    // Create entry and add to list
    var newEntry = new YourEntry(/* params */);
    Entries.Add(newEntry);

    // Close and reset
    await CloseRecordModal();
}

// Form class
public class YourFacilityRecordForm
{
    public string SomeField { get; set; } = string.Empty;
    public DateTime DateField { get; set; } = DateTime.Now;
    // ... add your properties
}
```

## Example: Implementing for Each Facility

### **Slaughterhouse (SH.razor)** ✅ Already Implemented
```
Record Slaughter Button
- Owner Name
- Animal Type (Hog, Carabao, Cow)
- Number of Heads
- OR Number
- Date
```

### **New Public Market (NPM.razor)**
```
Record Sale Button
- Vendor Name / Stall Number
- Item Type (Vegetable, Fish, Meat, etc.)
- Quantity
- Amount
- OR Number
- Date
```

### **Tampak Commercial Center (TCC.razor)**
```
Record Transaction Button
- Tenant Name / Unit Number
- Transaction Type (Rent, Utility, Other)
- Amount
- OR Number
- Date
```

### **New Commercial Center (NCC.razor)**
```
Record Transaction Button
- Tenant Name / Unit Number
- Amount
- OR Number
- Date
```

### **BBQ Stand (BBQ.razor)**
```
Record Transaction Button
- Stand Holder Name
- Daily Collection
- Amount
- Date
```

### **Iceplant (ICEPLANT.razor)**
```
Record Transaction Button
- User Name
- Ice Quantity / Blocks
- Amount
- OR Number
- Date
```

## CSS Classes Available

The modal comes with pre-styled classes:

```css
/* Form structure */
.form-section-divider    /* Section container */
.form-section-label      /* "SECTION TITLE" text */
.form-row-2              /* Two-column grid */
.form-row-full           /* Full-width field */
.form-group              /* Field wrapper */
.form-label              /* Label text */
.form-input              /* Text input */
.form-select             /* Select dropdown */
.form-textarea           /* Text area */
.form-error              /* Error message box */
.req                     /* Required asterisk (red) */

/* Buttons in footer */
.btn-ghost               /* Cancel button */
.btn-primary             /* Submit button */
```

## Color Scheme (Consistent with App)
- Primary: `var(--navy)` (#0d2137)
- Accent: `var(--gold)` (#c8a84b)
- Success: `var(--green)` 
- Error: `var(--red)`
- Backgrounds: `var(--bg)`, `var(--bg-card)`, `var(--bg-icon)`

## Steps to Apply to Your Facility

1. **Copy the SH.razor pattern** into your facility page
2. **Update form fields** to match your facility type
3. **Modify validation logic** in `SubmitRecord()`
4. **Update button text** and modal title in the component tag
5. **Test the modal** - add an entry and verify it appears in the table

## Notes
- All form validation should be done in the `SubmitRecord()` method
- Error messages are automatically displayed in the modal
- The modal closes automatically after successful submission
- Child content (form fields) is fully customizable per facility
- The component is responsive and works on mobile devices

## Component Props

| Prop | Type | Required | Description |
|------|------|----------|-------------|
| `ShowModal` | `bool` | Yes | Controls modal visibility |
| `OnClose` | `EventCallback` | Yes | Fired when modal closes |
| `OnSubmit` | `EventCallback` | Yes | Fired when submit button clicked |
| `ModalTitle` | `string` | No | Header title (default: "Add Record") |
| `ModalSubtitle` | `string` | No | Header subtitle |
| `SubmitButtonText` | `string` | No | Submit button label (default: "Add Record") |
| `ErrorMessage` | `string` | No | Error text to display |
| `ChildContent` | `RenderFragment` | No | Form fields content |
