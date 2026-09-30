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
Implemented in this pass: Transportation vehicle-class Cash Ticket, approved-animal Slaughterhouse selection, WCF without meter or rate lines, tenant-branded Profile, neutral connection errors. Not yet implemented: the light shell/tokens, "Today's Work" Menu, Login restyle, Records and Reports redesign, collector position (blocked by a backend gap). See the functional audit and the backend gaps.
