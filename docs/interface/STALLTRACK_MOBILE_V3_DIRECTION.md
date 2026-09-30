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
- Amounts, rates, animals, vehicle classes and instruments come from the server. The collector never types a rate or picks an instrument the server did not offer.
- The business date is the server's date (`Session.Menu.Today`).
- Money and forms are shown apart: ticket counts are never pesos; Collected, Remitted and Unremitted come from the server. Mobile cannot record or void a remittance.
- Offline: a physically issued OR/CT is queued first and never returns to the available list; failed and needs-review items stay visible.

## Status of this direction
Implemented: `--mobile-*` tokens, light compact header (server business date), quiet bottom nav, light Login and splash, task-first Menu (Available now / Assigned, not available / Needs attention), Reports Position tab, Records with canonical operation collections, Profile with assigned operations and sync status, Transportation vehicle-class CT, approved-animal Slaughterhouse, WCF without meter or rate lines.

NPM, the monthly rental family (TCC, NCC, BBQ, Ice Plant) and Tabo now share one field layout: compact header with the server facility name and period or market-day state, one context strip, sticky search and filter, flat divider rows with aligned amounts, 48px inputs and actions, and an on-device acknowledgement that states it does not replace the pre-numbered Official Receipt. The monthly sheet shows the server's obligation, paid and remaining. Tabo names its fee "Tabo vendor fee". Their `@code` blocks are unchanged apart from the display-name helper.

Not done: removal of the legacy palette variables (unmigrated rules still use them), and any Android runtime or visual review (to be done by the product owner on a Windows machine). See the functional audit and backend gaps.
