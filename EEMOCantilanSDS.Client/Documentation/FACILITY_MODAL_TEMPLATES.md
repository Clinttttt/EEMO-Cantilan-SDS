# Facility Record Modal - Copy-Paste Templates

Use these templates as a starting point for each facility. Just copy the patterns and customize the fields.

## Template 1: Slaughterhouse Pattern (SH.razor) ✅

Already implemented - reference this as your guide!

## Template 2: NPM (New Public Market)

### In the Toolbar Section:
```razor
<button class="btn-primary" @onclick="OpenRecordNPMModal">
    <svg viewBox="0 0 24 24"><!-- Plus icon SVG --></svg>
    Record Sale
</button>
```

### Add the Modal:
```razor
<FacilityRecordModal 
    ShowModal="ShowNPMModal" 
    OnClose="CloseNPMModal"
    OnSubmit="SubmitNPMRecord"
    ModalTitle="Record Market Sale"
    ModalSubtitle="Enter the sales transaction details for New Public Market."
    SubmitButtonText="Add Sale"
    ErrorMessage="@ErrorMessage">
    
    <div class="form-section-divider">
        <div class="form-section-label">Transaction Date</div>
        <div class="form-row-full">
            <div class="form-group">
                <label class="form-label">Date <span class="req">*</span></label>
                <input type="date" class="form-input" @bind="NPMForm.TransactionDate" />
            </div>
        </div>
    </div>

    <div class="form-section-divider">
        <div class="form-section-label">Vendor Information</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Vendor Name / Stall <span class="req">*</span></label>
                <input type="text" class="form-input" placeholder="e.g. Stall 01 - Rodriguez" @bind="NPMForm.VendorName" />
            </div>
            <div class="form-group">
                <label class="form-label">OR Number <span class="req">*</span></label>
                <input type="text" class="form-input" placeholder="e.g. OR-2026-NPM-001" @bind="NPMForm.ORNo" />
            </div>
        </div>
    </div>

    <div class="form-section-divider">
        <div class="form-section-label">Sales Details</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Item Type <span class="req">*</span></label>
                <select class="form-input" @bind="NPMForm.ItemType">
                    <option value="">-- Select Item Type --</option>
                    <option value="Vegetable">Vegetable</option>
                    <option value="Fish">Fish</option>
                    <option value="Meat">Meat</option>
                    <option value="Other">Other</option>
                </select>
            </div>
            <div class="form-group">
                <label class="form-label">Amount (₱) <span class="req">*</span></label>
                <input type="number" class="form-input" min="0" placeholder="0.00" @bind="NPMForm.Amount" />
            </div>
        </div>
    </div>

</FacilityRecordModal>
```

### In @code Section:
```csharp
private bool ShowNPMModal { get; set; }
private NPMRecordForm NPMForm = new();

private void OpenRecordNPMModal()
{
    NPMForm = new();
    ErrorMessage = string.Empty;
    ShowNPMModal = true;
}

private async Task CloseNPMModal()
{
    ShowNPMModal = false;
    NPMForm = new();
    ErrorMessage = string.Empty;
}

private async Task SubmitNPMRecord()
{
    if (NPMForm.TransactionDate == default)
    {
        ErrorMessage = "Please select a transaction date.";
        return;
    }
    if (string.IsNullOrWhiteSpace(NPMForm.VendorName))
    {
        ErrorMessage = "Vendor name is required.";
        return;
    }
    if (string.IsNullOrWhiteSpace(NPMForm.ItemType))
    {
        ErrorMessage = "Please select an item type.";
        return;
    }
    if (NPMForm.Amount <= 0)
    {
        ErrorMessage = "Amount must be greater than 0.";
        return;
    }
    if (string.IsNullOrWhiteSpace(NPMForm.ORNo))
    {
        ErrorMessage = "OR number is required.";
        return;
    }

    // TODO: Create and add NPMEntry to your list
    // var newEntry = new NPMEntry(...);
    // Entries.Add(newEntry);

    await CloseNPMModal();
}

public class NPMRecordForm
{
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public string VendorName { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ORNo { get; set; } = string.Empty;
}
```

---

## Template 3: TCC (Tampak Commercial Center)

```razor
<!-- BUTTON -->
<button class="btn-primary" @onclick="OpenRecordTCCModal">
    <svg viewBox="0 0 24 24"><!-- Plus icon SVG --></svg>
    Record Transaction
</button>

<!-- MODAL -->
<FacilityRecordModal 
    ShowModal="ShowTCCModal" 
    OnClose="CloseTCCModal"
    OnSubmit="SubmitTCCRecord"
    ModalTitle="Record TCC Transaction"
    ModalSubtitle="Enter the commercial center transaction details."
    SubmitButtonText="Add Transaction"
    ErrorMessage="@ErrorMessage">
    
    <div class="form-section-divider">
        <div class="form-section-label">Transaction Date</div>
        <div class="form-row-full">
            <div class="form-group">
                <label class="form-label">Date <span class="req">*</span></label>
                <input type="date" class="form-input" @bind="TCCForm.TransactionDate" />
            </div>
        </div>
    </div>

    <div class="form-section-divider">
        <div class="form-section-label">Tenant Information</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Tenant Name / Unit <span class="req">*</span></label>
                <input type="text" class="form-input" placeholder="e.g. Unit 101 - Torres" @bind="TCCForm.TenantName" />
            </div>
            <div class="form-group">
                <label class="form-label">OR Number <span class="req">*</span></label>
                <input type="text" class="form-input" placeholder="e.g. OR-2026-TCC-001" @bind="TCCForm.ORNo" />
            </div>
        </div>
    </div>

    <div class="form-section-divider">
        <div class="form-section-label">Payment Details</div>
        <div class="form-row-2">
            <div class="form-group">
                <label class="form-label">Transaction Type <span class="req">*</span></label>
                <select class="form-input" @bind="TCCForm.TransactionType">
                    <option value="">-- Select Type --</option>
                    <option value="Rent">Rental Fee</option>
                    <option value="Utility">Utility Charges</option>
                    <option value="Maintenance">Maintenance</option>
                    <option value="Other">Other</option>
                </select>
            </div>
            <div class="form-group">
                <label class="form-label">Amount (₱) <span class="req">*</span></label>
                <input type="number" class="form-input" min="0" placeholder="0.00" @bind="TCCForm.Amount" />
            </div>
        </div>
    </div>

</FacilityRecordModal>
```

```csharp
private bool ShowTCCModal { get; set; }
private TCCRecordForm TCCForm = new();

private void OpenRecordTCCModal() { TCCForm = new(); ErrorMessage = string.Empty; ShowTCCModal = true; }
private async Task CloseTCCModal() { ShowTCCModal = false; TCCForm = new(); ErrorMessage = string.Empty; }

private async Task SubmitTCCRecord()
{
    if (TCCForm.TransactionDate == default) { ErrorMessage = "Please select a date."; return; }
    if (string.IsNullOrWhiteSpace(TCCForm.TenantName)) { ErrorMessage = "Tenant name is required."; return; }
    if (string.IsNullOrWhiteSpace(TCCForm.TransactionType)) { ErrorMessage = "Transaction type is required."; return; }
    if (TCCForm.Amount <= 0) { ErrorMessage = "Amount must be greater than 0."; return; }
    if (string.IsNullOrWhiteSpace(TCCForm.ORNo)) { ErrorMessage = "OR number is required."; return; }

    // TODO: Create and add TCCEntry
    await CloseTCCModal();
}

public class TCCRecordForm
{
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public string TenantName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ORNo { get; set; } = string.Empty;
}
```

---

## Template 4: NCC (New Commercial Center)

```razor
<!-- Similar to TCC, just change titles and field names -->
<button class="btn-primary" @onclick="OpenRecordNCCModal">
    Record Payment
</button>

<FacilityRecordModal 
    ShowModal="ShowNCCModal" 
    OnClose="CloseNCCModal"
    OnSubmit="SubmitNCCRecord"
    ModalTitle="Record NCC Transaction"
    ModalSubtitle="Enter the payment details for New Commercial Center."
    SubmitButtonText="Add Payment"
    ErrorMessage="@ErrorMessage">
    
    <!-- Use similar form structure as TCC -->

</FacilityRecordModal>
```

---

## Template 5: BBQ Stand & Iceplant

Follow the same pattern as TCC - adjust field names and labels accordingly.

**BBQ Fields:**
- Date, Stand Holder Name, Collection Amount, OR Number

**Iceplant Fields:**
- Date, User Name, Quantity (blocks/units), Amount, OR Number

---

## Quick Checklist for Each Facility

- [ ] Copy the Modal component tag
- [ ] Update `ModalTitle` and `ModalSubtitle` 
- [ ] Rename `ShowModal`, form variable, and methods
- [ ] Add facility-specific form fields
- [ ] Implement validation in `SubmitRecord()`
- [ ] Create a form class with your properties
- [ ] Create an entry class/record for your data model
- [ ] Test adding a record

That's it! 🚀
