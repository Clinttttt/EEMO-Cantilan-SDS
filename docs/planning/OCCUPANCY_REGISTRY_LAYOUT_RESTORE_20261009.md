# Occupancy Registry original-layout restoration — 2026-10-09

## Authority and scope
Local checkout: `C:\dev\stalltrack\eemo`, existing `codex/vegetable-fruit-space-workspace` branch. User's hosted /vendors screenshot is the layout reference, **not** an instruction to copy hosted data or merge payor accounts.

## Accepted presentation change
- Restore the compact original /vendors selector → All/Paid/Partial/Unpaid/Closed → search → count/Add Occupant toolbar. Remove the new full-width Facility / Operation selector and stacked second toolbar for the stall-facility registry.
- Preserve the existing Occupancy Registry hero, eight-column registered table, NPM/NCC/TCC/BBQ/ICE facility source records, and facility action handlers.
- Add approved KM (Kanmanggay), VF (Vegetable/Fruits Monthly), and FA (Fiesta/Araw) to that original facility selector **when operation discovery returns their source kinds**.
- For these operation entries use their existing obligation-source adapters and the same shared civic hero, compact registry toolbar, registered table and action conventions. The selected source keeps its own authoritative account identity, statuses and balances. Its lifecycle/payment filters remain independent.
- All links and existing direct account routes continue to work. No duplicate registry or cross-source Collection writer.

## Financial and display boundaries
The stall-facility-wide VendorRegistryDto hero figures do not silently include KM/VF/FA obligation account amounts. Their summary figures are source-specific when selected; adding visual choices is **not** a cross-source financial consolidation. The global hero labels were scoped to registered stall facilities to avoid implying otherwise. Do not add event-lot amounts to monthly stall rental targets.

The account actions are view position (₱), canonical payment history (↺), and open account details (→); authorized source-specific account changes remain inside details. No new receipt creation or unsupported reopening/renewal is implied.

## Verification
Client Release build completed with zero errors (existing warnings); the final full Web component test run passed **844/844**, including targeted compact-toolbar and source-adapter regressions. `git diff --check` passed.

Authenticated live-tenant and browser visual acceptance have not been performed; locally running Client may need a restart to load the new code. No API or persistence change, migration, deploy, push or integration-branch merge was performed. The three pre-existing concurrent planning-document edits must remain unstaged.
