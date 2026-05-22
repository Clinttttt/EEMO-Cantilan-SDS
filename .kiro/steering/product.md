# Product Overview

## EEMO Revenue Collection System

**Client:** Municipality of Cantilan, Surigao del Sur  
**Purpose:** Digital revenue collection system for government-managed facilities

## What It Does

Digitizes stall rental collection and payment tracking across 6 municipal facilities:

1. **New Public Market (NPM)** - Daily collection (₱30/day + utilities)
2. **Tampak Commercial Center (TCC)** - Monthly rental (₱2,400-₱4,800)
3. **New Commercial Center (NCC)** - Monthly rental (₱1,200-₱3,840)
4. **Barbecue Stand (BBQ)** - Monthly space rental (₱1,600-₱9,600)
5. **Iceplant (ICE)** - Monthly space rental (₱1,000-₱2,000)
6. **Slaughterhouse (SLH)** - Per-head fees (Hog ₱250, Large animals ₱365)

## Key Features

- **Multi-facility management** - Track stalls, contracts, and payments across all facilities
- **Payment tracking** - Record monthly payments, partial payments, and payment history
- **Daily collections** - NPM-specific daily fee collection with fish weight tracking
- **Contract management** - Track occupants, contract terms, and expiry dates
- **Delinquency tracking** - Automatic status calculation based on payment history
- **Slaughterhouse transactions** - Per-head billing for different animal types
- **Role-based access** - SuperAdmin, Admin, and Collector roles with facility assignments
- **Mobile support** - .NET MAUI app for field collectors (future phase)

## User Roles

- **SuperAdmin** - System setup, admin account creation
- **Admin** - Full facility management, payment recording, reporting
- **Collector** - Mobile field collection, assigned to specific facilities

## Business Rules

- OR numbers are manually entered by admins (never auto-generated)
- Delinquent status: 3+ months unpaid in rolling 12-month window
- Contract expiry warning: 3 months before expiration
- Account lockout: 5 failed login attempts = 15 minute lock
- All fee rates defined in `FeeRates` constants - never hardcoded
