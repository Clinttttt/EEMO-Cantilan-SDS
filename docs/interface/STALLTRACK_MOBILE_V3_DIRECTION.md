# StallTrack Mobile V3 Direction

The Collector Mobile is a client of server-owned rules. It shares the V3 identity with the Web Office portal but is built for one hand, outdoors, on a small screen and an unreliable connection.

## Principles
- Light, calm surface; deep navy for key text; civic blue only for the active or actionable element; muted gold as a small accent.
- DM Sans only. Tabular numbers for money. No gradients, glows or glass. Modest radius.
- Registers and dividers over stacks of cards. One idea per screen; fewer badges and eyebrows.
- Touch targets of at least 44px; safe-area padding; usable from 360 to 430px wide.
- Bottom navigation: Menu, Records, Reports, Profile.
- Tenant name, seal and office come from branding, never from markup.

## Behavior rules
- Assignment is not collectibility. Only a Ready capability collects; other states show one mapped sentence.
- Rates, animals, configured choices and instruments come from the server. A collector may type an **amount received** only when the server capability explicitly offers a Direct amount rule; the collector never invents a rate or instrument.
- The business date is the server's date (`Session.Menu.Today`).
- Money and forms are shown apart: ticket counts are never pesos; Collected, Remitted and Unremitted come from the server. Mobile cannot record or void a remittance.
- Offline collection queues preserve stable client/session/item identity and wait for the server-issued SRC. The device never invents an SRC and physical OR/CT stock never gates collection under IA-062.

## Status of this direction
Implemented before the 2026-10-06 office clarification: `--mobile-*` tokens, light compact header (server business date), quiet bottom nav, light Login and splash, task-first Menu, Reports Position tab, Records with canonical operation collections, Profile with assigned operations and sync status, Transportation vehicle-class CT, approved-animal Slaughterhouse, WCF without meter or rate lines.

**Implementation note (2026-10-07):** the accepted local integration checkpoint now implements the clarified split: Income From Terminal owns its Terminal sections and vehicle-assisted entry, while Transportation/Parking is direct amount. Deployed production may still reflect the older behavior until an explicit rollout; historical Transportation/TRM records are not reclassified by guess.

NPM, the monthly rental family (TCC, NCC, BBQ, Ice Plant) and Tabo now share one field layout: compact header with the server facility name and period or market-day state, one context strip, sticky search and filter, flat divider rows with aligned amounts, 48px inputs and actions, and an on-device acknowledgement that states it does not replace the pre-numbered Official Receipt. The monthly sheet shows the server's obligation, paid and remaining. Tabo names its fee "Tabo vendor fee". Their `@code` blocks are unchanged apart from the display-name helper.

Not done: removal of the legacy palette variables (unmigrated rules still use them), and any Android runtime or visual review (to be done by the product owner on a Windows machine). See the functional audit and backend gaps.

## Startup / launch experience (2026-10-01)

- **Native splash** (`MauiSplashScreen`): the StallTrack icon (the launcher foreground, sized for the adaptive safe zone)
  on the Mobile V3 canvas `#eef3f9` (`--mobile-canvas`; native resources cannot read CSS variables, so the literal is
  documented here). No copy, no second logo, no animation beyond the system's own.
- **Blazor launch** (`Home.razor` + `Home.razor.css`): the continuation of the native splash, not a second splash — the
  seal at a similar scale, the STALLTRACK wordmark, "Collector Mobile" and a 24px gold rule on the same canvas. No
  background image, no glow, no gradient, no Bagong Pilipinas block, no marketing copy.
- **Host**: `MainPage` and the `BlazorWebView` paint the same canvas, so the hand-over shows no white or dark flash in
  light or dark system mode. The launch stays light; Collector V3 has no dark theme.
- **Timing**: session restore starts on first render; there is no artificial minimum (the old 600 ms wait and 600 ms
  label cycling are gone). Nothing is said for the first 400 ms; then "Starting securely…", and after 2.5 s once
  "Restoring your session…" (`LaunchState`). A small civic-blue indeterminate indicator, never fake progress.
- **Routing** is unchanged: restored → `/menu`; otherwise, including expired sessions and network failure → `/login`.
- **Tenant-neutral**: launch never shows municipal branding, so a device bound to another LGU never sees Cantilan first.
- **Motion**: opacity fade only (≤0.24 s); `prefers-reduced-motion` removes it and slows the indicator.
- **Assets**: the only launch image is `stalltrack-seal.png`, which Login already needs; the 148 KB background WebP was
  removed from the app and the Bagong Pilipinas logo is no longer loaded at start (the asset itself is kept).

## Selection controls (2026-10-01)

- One shared control, `Components/Shared/MobileChoice.razor`, replaces native `<select>` controls on Collector Mobile where a compact bounded choice is appropriate.
- Default behavior remains inline-in-flow: a 48px trigger states label, current choice and optional read-only detail; the selected row carries a check and "Selected" text; civic-blue focus/selected state; disabled, error and empty states.
- A backward-compatible **opt-in popover mode** is allowed where an inline expansion would incorrectly move important content. NPM Whole Payment Month/Year use this mode: the option list overlays from the trigger, selection closes it, Month may scroll internally, and opening it must not shift the payment summary below. Existing callers remain inline unless they explicitly opt in.
- Short two-way choices stay option cards (Vegetable/Fruit Whole payment | Daily collection). Long lists (payors, stalls,
  sources) use a searchable list, never a dropdown (WCF `/wcf`).
- No rate or amount is typed in a selector; edit flows keep a historical value as a labelled read-only choice.


## 2026-10-06 authoritative collection direction

The office clarification changes Mobile information architecture and New Collection behavior.

### Menu organization

Organize collection work using the official Monthly Income families rather than arbitrary technical buckets:

- **Income From Market**
- **Rent Income (Stall Rental)**
- **Space Rental**
- **Income From Terminal**
- **Income From Slaughterhouse**

The Menu may still collapse/expand these for a small screen, but the labels and ownership should align with the official report structure.

Transportation/Parking remains under the Market revenue family and is not a Terminal/TRM synonym.

### Source-native search

New Collection should search authoritative source records, not a generic Business Payor master.

Searchable source examples:

- NPM occupants/stallholders;
- monthly rental occupants/accounts;
- Kanmanggay / space holders;
- Fish/Meat vendor registrations;
- utility subjects/accounts;
- other approved operation-owned identities.

A result shows its source context so equal names remain distinguishable.

Do not auto-merge or link records because their names match.

### Relevant-items-first behavior

After selecting a source record, Add Item shows only the operations genuinely available for that source.

Examples:

- registered Fish vendor → Fish/Meat Vendor Fee and Weight & Measure;
- Kanmanggay holder → current monthly space-rental obligation;
- NPM occupant → today's Daily charge, valid Whole Payment, and only utilities that actually exist for that source.

The client never creates eligibility by display logic.

### Direct fallback

If search finds no source relationship, the collector may continue only with operations whose server policy allows an optional/free-text payer/reference.

This fallback is appropriate for direct/one-off revenue such as sources explicitly configured for walk-up collection.

It is not valid for:

- monthly rent;
- source-backed utility obligations;
- Weight & Measure;
- any other operation that requires a registered/source-owned relationship.

### Fish/Meat and Weight & Measure

Fish/Meat Vendor Fee is independent from NPM.

- Search/select the independent vendor registry when available.
- The vendor type is Fish or Meat.
- Fee amount is direct amount received; there is no fixed rate.
- If a fee is collected before registration exists, the vendor name may be recorded as snapshot text.

Weight & Measure remains a separate classification.

- It requires a registered Fish/Meat vendor.
- No free-text unregistered-vendor fallback.
- Vendor type determines which weighing context/rate choice is valid.
- It may remain as a standalone Mobile route and also appear inside New Collection for an eligible registered vendor.
- Both entry routes must call the same backend writer.

### Terminal

Income From Terminal is a separate Mobile operation family:

- Comfort Room;
- Pull Pul Vans, Cargo Vans;
- Tricycad.

All use Cash Ticket and support direct aggregate amount entry.

Cash Ticket count is optional supporting input.

Vehicle-class-assisted entry belongs here:

- Jeepney, Multicab, Van, Public Utility Bus, Public Utility Baby Bus → Pull Pul Vans, Cargo Vans;
- Tricycle → Tricycad.

### Transportation / Parking

Transportation/Parking is separate from Terminal.

Mobile target:

- CT;
- direct amount received;
- no required vehicle-class selector;
- no TRM terminology.

### NPM Daily Collect All

Provide a reviewed fast path for **today's NPM Daily charge only**.

Flow:

1. open eligible stalls for the server business date;
2. eligible rows may start selected;
3. collector unchecks exceptions/absent payers;
4. show selected count and total;
5. review;
6. post one child Collection/SRC per selected stall.

Unchecked stalls remain unpaid for the day.

Do not use this for NPM Whole Payment or for TCC/NCC/BBQ/ICE/Kanmanggay monthly balances.

### Business Payor retirement

Do not expose Business Payor as a required Mobile concept.

Existing internal IDs may remain during migration, but the visible product and future API contracts should move toward source-native identities under ADR-007 / IA-068.
