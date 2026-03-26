# EEMO Revenue Collection System — Project Documentation
**Client:** Economic Enterprise and Management Office (EEMO), Municipality of Cantilan  
**Stack:** Blazor Server (.NET) — Web Admin Dashboard + Mobile App (later phase)

---

## Problem
Staff manually re-types vendor names, dates, and details every time they record a payment. Very tedious and error-prone.

## Solution
A system where vendor/lessee records are pre-loaded. Staff just **tick/check** who paid and input the amount — no need to retype names or dates. Auto-generates reports and flags delinquents.

---

## Facilities & Fee Types

| Facility | Code | Payment Type |
|----------|------|-------------|
| New Public Market | NPM | Daily (₱30/day per stall) |
| Tampak Commercial Center | TCC | Monthly rental |
| New Commercial Center | NCC | Monthly rental |
| Barbecue Stand | BBQ | Monthly rental |
| Iceplant | ICE | Monthly rental |
| Slaughterhouse | SLH | Per head (see rates below) |
| Miscellaneous | Misc. ECF / WCF | Varies |

---


## Key Fee Details

**NPM (New Public Market)** — 3 sections: Vegetable, Meat, Fish  
- ₱30/day per stall + electricity + water  
- Fish section: extra ₱1/kilo of fish sold  
- Stall area: ~4.8 sq.m | Monthly contract: ₱900

**TCC / NCC** — Monthly rent, 3-year contracts (started 6/7/2023)  
- Stall sizes vary (10.5, 17.5, 35, 70 sq.m)  
- Rates: ₱900 – ₱4,800/month depending on size & location (corner slots cost more)

**Slaughterhouse fees (per head):**
- Hogs: ₱250 total (Slaughter ₱50 + Ante Mortem ₱20 + Table Charge ₱30 + Entrance ₱150)
- Carabao/Cow: ₱365 total (Slaughter ₱150 + Permit ₱100 + Ante Mortem ₱20 + Post Mortem ₱25 + Table ₱30 + Livestock ₱40)

---

## Data Model Notes
- Each stall has: **Actual Occupant** (real person) + **Signed Lessee** (on contract) — these can differ
- Some stalls are **Closed** or have **No Contract (space only)**
- System must track: stall no., occupant, contract date, area, monthly rate, payment status, delinquency

---

## Web Admin Dashboard (Current Focus)
- Built in **Blazor Server** (Razor Pages)
- Professional government look
- Features: Dashboard overview, per-facility collection lists (checkable), payment recording, reports, delinquency tracking
- Entry point page: `@page "/menu"` — show the main menu/navigation

## Mobile App
- Planned for later phase
- Should share similar UI vibe as the web dashboard
