# Product Overview

## EEMO Revenue Collection System

**Client:** Economic Enterprise and Management Office (EEMO), Municipality of Cantilan, Surigao del Sur
**Purpose:** Digital revenue collection system replacing a manual, paper-based process for government-managed facilities

## What It Does

Digitizes fee collection, payment tracking, delinquency monitoring, and reporting across **8 municipal facilities**:

1. **New Public Market (NPM)** — Daily collection (₱30/day + electricity + water; Fish section adds ₱1/kg)
2. **Tampak Commercial Center (TCC)** — Monthly rental (₱2,400–₱4,800)
3. **New Commercial Center (NCC)** — Monthly rental (Extension ₱1,200, Corner ₱3,240–₱3,840)
4. **Barbecue Stand (BBQ)** — Monthly space rental (₱1,600–₱9,600)
5. **Iceplant (ICE)** — Monthly space rental (₱1,000–₱2,000)
6. **Slaughterhouse (SLH)** — Per-head fees (Hog ₱250, Large animals/Carabao/Cow ₱365)
7. **Transport Terminal (TRM)** — ₱30 per trip (paid by driver) with trip queuing/dispatch order
8. **Tabo-an Public Market (TPM)** — ₱100 per vendor per market day (every Friday)

> Per-stall **MonthlyRate**, **DailyRate**, and **AreaSqm** are stored per stall (flexible, admin-entered). Fixed ordinance rates (NPM daily, fish/kg, SLH per-head, TPM, TRM) live in the `FeeRates` constants.

## Key Features

- **Multi-facility management** — stalls, contracts, occupants, and payments across all facilities
- **Payment tracking** — Paid / Partial / Unpaid per stall per period, with payment history
- **Daily collections (NPM)** — calendar-style daily fee marking with fish-weight tracking
- **Trip queuing (TRM)** — per-day trip numbering and departure order
- **Weekly market (TPM)** — Friday-only vendor attendance and per-vendor collection
- **Slaughterhouse transactions** — per-head billing per animal type (incl. custom animals)
- **Contract management** — occupant vs. signed lessee, terms, and expiry tracking
- **Delinquency tracking** — automatic status from a rolling 12-month window
- **Reports** — collection totals, outstanding, paid/unpaid counts, and collection rate per facility/period
- **Audit trail** — every financial transaction (payments, daily collections, TPM/TRM/SLH) is logged with actor, timestamp, and before/after values
- **Role-based access** — SuperAdmin/Admin (web) and Collector (mobile), with facility assignments

## Platforms

- **Web Admin Dashboard** — Admin / Head only (records, OR entry, reports, user management)
- **Mobile App (.NET MAUI)** — Collectors only, for field collection (later phase)

## User Roles

- **SuperAdmin (Head)** — system setup, admin/collector account creation, full access
- **Admin** — facility management, payment recording, OR-number entry, reporting (web)
- **Collector** — field collection on mobile; assigned to specific facilities

## Business Rules

- **Web is admin/head only**; collectors authenticate on the **mobile** app (field use)
- **Collector attribution** — `CollectorId` records the collector who collected; it is taken from the authenticated user, never from the client request. Admin-recorded entries leave `CollectorId` null (the admin is captured in audit fields instead)
- OR numbers are manually entered by admins (never auto-generated); adding an OR number never alters the original collector/timestamp
- Delinquent status: 3+ unpaid months in a rolling 12-month window; 1–2 = arrears
- Contract expiry warning: within 3 months of expiration
- Account lockout: 5 failed login attempts = 15-minute lock
- Access tokens expire in 15 minutes; refresh tokens are hashed at rest, single-source, and revoked on logout
- Business-day logic (today, current month, contract expiry, streaks, trip-day) uses **Philippine time (UTC+8)**; stored timestamps stay in UTC
- All fixed fee rates defined in `FeeRates` constants — never hardcoded in handlers
