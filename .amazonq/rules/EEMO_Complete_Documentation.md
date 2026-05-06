# EEMO Revenue Collection System
**Economic Enterprise and Management Office**  
**Municipality of Cantilan, Surigao del Sur**

---

## Executive Summary

The EEMO Revenue Collection System is a digital platform developed for the Economic Enterprise and Management Office of the Municipality of Cantilan, Surigao del Sur. The system replaces a fully manual, paper-based revenue collection process with a streamlined digital workflow — enabling staff to record payments faster, track delinquent vendors, and generate accurate reports with minimal effort.

---

## Background

The EEMO oversees eight government-managed facilities within the municipality that generate revenue through stall rentals, daily market fees, slaughterhouse charges, transport terminal fees, and weekly market vendor fees. Prior to this system, collection staff manually wrote or re-typed vendor names, stall details, and payment information every single transaction — a process that was slow, repetitive, and highly prone to human error. Reports had to be compiled by hand, and identifying delinquent vendors required going through records one by one.

---

## Problem Statement

- Staff manually re-enter the same vendor and stall information for every payment transaction
- No centralized record of who has paid, who hasn't, and who is delinquent
- Report generation is done manually and takes significant time
- No audit trail for payments and collections
- Difficult to monitor multiple facilities simultaneously
- Risk of lost or inaccurate records due to paper-based process

---

## Objectives

1. Eliminate manual re-entry of vendor and stall data during payment collection
2. Provide a real-time view of payment status across all facilities
3. Automatically flag delinquent vendors based on payment history
4. Generate collection reports without manual computation
5. Maintain a complete and accurate audit trail of all transactions
6. Enable both office-based (web) and field-based (mobile) collection workflows

---

## Scope

### In Scope

- Management of vendor and stall records across all 8 EEMO facilities
- Recording and tracking of daily, weekly, monthly, per-head, and per-trip fee collections
- Trip queuing management for the Transport Terminal
- Payment status monitoring (Paid, Partial, Unpaid) per vendor per period
- Delinquency tracking and flagging
- OR (Official Receipt) number recording and management
- Contract management for stall lessees
- Report generation for collections per facility and per period
- User management for admin staff and field collectors
- Mobile application for field collectors (later phase)

### Out of Scope

- Online payment processing or e-wallet integration
- Accounting or general ledger functions beyond revenue collection
- Payroll or HR management
- Inventory management
- Integration with national government systems (PhilSys, BIR, etc.)

---

## Facilities Covered

| Facility | Description | Fee Type |
|----------|-------------|----------|
| New Public Market (NPM) | Main public market with three sections — Vegetable, Fish, and Meat | Daily fee per stall + utilities + fish fee per kilo |
| Tampak Commercial Center (TCC) | Commercial stall complex with monthly rental contracts | Monthly rental |
| New Commercial Center (NCC) | Commercial center with extension and corner stall classifications | Monthly rental |
| Barbecue Stand (BBQ) | Designated barbecue vendor stalls | Monthly rental |
| Iceplant (ICE) | Ice production and distribution facility | Monthly rental |
| Slaughterhouse (SLH) | Municipal slaughterhouse for hogs, carabao, and cattle | Per head slaughter fee |
| Transport Terminal (TRM) | Municipal transport terminal managing driver departures and trip queuing | ₱30 per trip (paid by driver) |
| Tabo-an Public Market (TPM) | Weekly public market held every Friday where vendors sell food and goods | ₱100 per vendor per market day (every Friday) |

---

## Fee Details

### New Public Market (NPM)
- Operates in three sections: **Vegetable**, **Meat**, and **Fish**
- ₱30/day per stall + electricity + water charges
- Fish section stalls are additionally charged **₱1 per kilo** of fish sold
- Stall area: approximately **4.8 sq.m**
- Monthly stall contract rate: **₱900**

### Tampak Commercial Center (TCC) & New Commercial Center (NCC)
- Monthly rental under **3-year contracts** (contracts started **June 7, 2023**)
- Stall sizes available: **10.5, 17.5, 35, and 70 sq.m**
- Monthly rates range from **₱900 to ₱4,800** depending on stall size and location
- Corner slots are priced higher than standard slots

### Slaughterhouse (SLH) — Fixed Fee Per Head

Each slaughter transaction is a fixed amount — the breakdown below reflects the components that make up that total.

| Animal | Fee Component | Amount |
|--------|--------------|--------|
| **Hogs** | Slaughter Fee | ₱50 |
| | Ante Mortem | ₱20 |
| | Table Charge | ₱30 |
| | Entrance Fee | ₱150 |
| | **Fixed Total** | **₱250** |
| **Carabao / Cow** | Slaughter Fee | ₱150 |
| | Permit | ₱100 |
| | Ante Mortem | ₱20 |
| | Post Mortem | ₱25 |
| | Table Charge | ₱30 |
| | Livestock Fee | ₱40 |
| | **Fixed Total** | **₱365** |

### Transport Terminal (TRM)
- ₱30 per trip, paid by the driver upon departure

### Tabo-an Public Market (TPM)
- ₱100 per vendor per market day (every Friday)

### Barbecue Stand (BBQ) & Iceplant (ICE)
- Monthly rental; rates based on individual stall contracts

---

## Data Model Notes

- Each stall record holds two distinct occupant fields:
  - **Actual Occupant** — the person physically operating the stall
  - **Signed Lessee** — the person named on the contract (these may differ)
- Stall statuses include: **Active**, **Closed**, and **No Contract (Space Only)**
- Each stall record tracks: stall number, occupant, contract date, stall area, monthly rate, payment status, and delinquency standing

---

## Key Features

### Weekly Market Collection (Tabo-an Public Market)
The Tabo-an Public Market operates every Friday as a public gathering where vendors sell food, goods, and other products. The system tracks vendor attendance and collects ₱100 per vendor each market day, replacing the manual list-based collection currently done on-site.

### Trip Queuing & Terminal Collection (Transport Terminal)
The Transport Terminal manages both fee collection and driver dispatch. Each driver pays ₱30 per trip, and the system tracks the queuing order — determining which driver departs next. This eliminates disputes over turn-taking and gives staff a clear, organized record of all trips and collections for the day.

### Vendor & Stall Registry
A centralized registry of all stalls and their occupants across every facility. Each stall record includes the actual occupant, the name on the contract (which may differ), stall area, monthly rate, contract dates, and current status.

### Payment Collection
Staff can quickly mark who has paid for the current period without retyping any information. Payments can be recorded as Paid, Partial, or Unpaid, and OR numbers from physical receipt books are logged against each transaction.

### Daily Collection Tracking (New Public Market)
The NPM operates on a daily collection model. The system provides a calendar view per stall where collectors mark each day as collected or missed. Fish section stalls additionally track kilos of fish sold for the per-kilo fee.

### Delinquency Monitoring
The system automatically identifies vendors who are behind on payments based on a rolling 12-month window. Vendors with 3 or more unpaid months are flagged as delinquent, while those with 1–2 unpaid months are marked as having arrears.

### Contract Management
Tracks the start date, duration, and expiry of each stall contract. Contracts expiring within 3 months are flagged as expiring soon, and expired contracts are clearly identified.

### Reports
Collection summaries can be generated per facility, per period, showing total collected, total outstanding, paid vs unpaid stall counts, and collection rates. Delinquent vendor reports can also be pulled for enforcement purposes.

### User Management
Two levels of system users: Admin staff (office-based, manages records and reports) and Collectors (field-based, records payments on mobile). Head has full control over account creation.

### Audit Trail
Every transaction and change in the system is logged with the actor, timestamp, and before/after values — providing a complete and tamper-evident history.

---

## Users

| Role | Description |
|------|-------------|
| Head | Full system access. Creates and manages admin accounts. |
| Admin | Manages vendor records, records OR numbers, generates reports. |
| Collector | Field staff. Marks payments on mobile app during rounds. |

---

## Workflow

```
Collector does rounds at the facility
    → Marks each stall as Paid or Partial on the mobile app
    → Physical OR (Official Receipt) is issued to the vendor

Back at the office:
    → Admin opens the web dashboard
    → Reviews the payment records submitted by collectors
    → Enters the OR number from the physical receipt book
    → System saves the complete payment record

End of period:
    → Admin generates collection report
    → Delinquent vendors are automatically listed
    → Report is ready for submission to the municipal treasurer
```

---

## Platforms

| Platform | Users | Purpose |
|----------|-------|---------|
| Web Admin Dashboard | Admin, Head | Full management — vendor records, OR entry, reports, user management |
| Mobile App | Collectors | Field collection — mark payments during daily rounds |

---

## Project Status

| Component | Status |
|-----------|--------|
| Web Admin Dashboard | In Development |
| Backend API | In Development |
| Mobile App | Planned (later phase) |

---

## Client Information

**Office:** Economic Enterprise and Management Office (EEMO)  
**Municipality:** Cantilan, Surigao del Sur  
**Region:** Region XIII (Caraga)