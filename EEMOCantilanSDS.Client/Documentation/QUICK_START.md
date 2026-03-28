# Quick Start - Apply Modal to Your Facility

## 5-Minute Setup Guide

### Step 1: Copy This Button
```razor
<button class="btn-primary" @onclick="OpenRecordModal">
    <svg viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>
    Record [Your Activity]
</button>
```
**Where**: In your facility page's toolbar section (next to "Search" box)

### Step 2: Add This Modal
```razor
<FacilityRecordModal 
    ShowModal="ShowRecordModal" 
    OnClose="CloseRecordModal"
    OnSubmit="SubmitRecord"
    ModalTitle="Record [Activity Name]"
    ModalSubtitle="Enter the details below."
    SubmitButtonText="Add Record"
    ErrorMessage="@ErrorMessage">
    
    <!-- COPY FROM FACILITY_MODAL_TEMPLATES.md -->
    <!-- Paste your facility's form fields here -->
    
</FacilityRecordModal>
```
**Where**: At the end of your page, before closing `</main>` tag

### Step 3: Add This Code Section
```csharp
@code {
    private bool ShowRecordModal { get; set; }
    private string ErrorMessage { get; set; } = string.Empty;
    private YourRecordForm RecordForm = new();
    
    private void OpenRecordModal()
    {
        RecordForm = new();
        ErrorMessage = string.Empty;
        ShowRecordModal = true;
    }

    private async Task CloseRecordModal()
    {
        ShowRecordModal = false;
        RecordForm = new();
        ErrorMessage = string.Empty;
    }

    private async Task SubmitRecord()
    {
        // COPY FROM FACILITY_MODAL_TEMPLATES.md
        // Add your validation and creation logic
    }

    public class YourRecordForm
    {
        // COPY FROM FACILITY_MODAL_TEMPLATES.md
        // Add your properties
    }
}
```

### Step 4: Customize for Your Facility
1. Change button text: "Record Slaughter" → "Record Sale" (etc.)
2. Change modal title and subtitle
3. Copy form fields from `FACILITY_MODAL_TEMPLATES.md`
4. Fill in validation and entry creation logic

### Step 5: Test
1. Click your button
2. Modal appears
3. Fill form
4. Click submit
5. Entry appears in table ✅

---

## Which Template Do I Use?

| Facility | Button Text | Form Fields | File |
|----------|------------|-------------|------|
| **NPM** | Record Sale | Vendor, Item Type, Amount, Date | TEMPLATES |
| **TCC** | Record Transaction | Tenant, Type, Amount, Date | TEMPLATES |
| **NCC** | Record Payment | Tenant, Amount, Date | TEMPLATES |
| **BBQ** | Record Collection | Holder, Amount, Date | TEMPLATES |
| **ICEPLANT** | Record Transaction | User, Quantity, Amount, Date | TEMPLATES |
| **SH** ✅ | Record Slaughter | Owner, Animal, Heads, Amount, Date | SH.razor |

---

## Files You Need

1. **FacilityRecordModal.razor** - Already created ✅
2. **FacilityRecordModal.razor.css** - Already created ✅
3. **FACILITY_MODAL_TEMPLATES.md** - Copy your facility's code
4. **SH.razor** - Reference implementation

---

## Common Gotchas

❌ **Forgot @bind on inputs**
- Form data won't save to RecordForm properties
- Solution: Add `@bind="RecordForm.PropertyName"` to all inputs

❌ **Forgot validation in SubmitRecord()**
- Invalid data might get added
- Solution: Check FACILITY_MODAL_TEMPLATES.md for validation examples

❌ **Wrong form class name**
- Form property won't bind
- Solution: Use consistent naming (e.g., `RecordForm.PropertyName`)

❌ **Forgot to close modal after submit**
- Modal stays open
- Solution: Call `await CloseRecordModal()` at end of SubmitRecord()

✅ **Check the working example (SH.razor)** if unsure!

---

## Need Help?

1. **How do I add the modal?** → Copy from Step 2 above
2. **What form fields do I need?** → Check FACILITY_MODAL_TEMPLATES.md
3. **How do I validate?** → See SH.razor SubmitRecord() method
4. **Does it work on mobile?** → Yes! Component is responsive
5. **Can I customize the colors?** → Yes! Edit FacilityRecordModal.razor.css

---

## Build Commands

```powershell
# Build solution
dotnet build

# Run dev server
dotnet run

# Clean build
dotnet clean
dotnet build
```

---

## Final Checklist Before Testing

- [ ] Button added to toolbar
- [ ] Modal component included
- [ ] StateVariables defined (ShowRecordModal, ErrorMessage, RecordForm)
- [ ] Methods created (OpenRecordModal, CloseRecordModal, SubmitRecord)
- [ ] Form class created with properties
- [ ] Validation logic implemented
- [ ] Entry creation logic implemented
- [ ] CSS classes used on form elements
- [ ] Build successful (no errors)

---

**Status**: Ready to implement! 🚀

Start with NPM (most complex) or BBQ (simplest). Reference SH.razor if stuck.

Happy coding!
