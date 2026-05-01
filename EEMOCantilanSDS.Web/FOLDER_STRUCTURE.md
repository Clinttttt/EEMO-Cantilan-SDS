# EEMOCantilanSDS.Web — Folder Structure

## Overview
React 19 + TypeScript + Vite frontend for EEMO Revenue Collection System.

---

## Folder Structure

```
src/
├── api/                           → API communication layer
│   ├── client.ts                  → Axios instance with interceptors
│   ├── endpoints.ts               → API endpoint constants
│   └── services/                  → API service functions
│       ├── authService.ts         → Login, logout, user management
│       ├── stallService.ts        → Stall CRUD operations
│       ├── paymentService.ts      → Payment operations
│       ├── dashboardService.ts    → Dashboard data
│       └── ...                    → One service per domain entity
│
├── components/                    → React components
│   ├── layout/                    → App shell components
│   │   ├── AdminLayout.tsx        → Main layout wrapper
│   │   ├── Sidebar.tsx            → Navigation sidebar
│   │   ├── Topbar.tsx             → Top navigation bar
│   │   └── ProtectedRoute.tsx    → Auth guard wrapper
│   │
│   ├── shared/                    → Generic reusable components
│   │   ├── Button.tsx             → Button with variants
│   │   ├── Modal.tsx              → Modal wrapper
│   │   ├── Input.tsx              → Form input with error display
│   │   ├── Select.tsx             → Dropdown with error display
│   │   ├── DataTable.tsx          → Generic data table
│   │   ├── KpiCard.tsx            → Dashboard KPI card
│   │   ├── Spinner.tsx            → Loading spinner
│   │   └── EmptyState.tsx         → Empty state message
│   │
│   └── features/                  → Domain-specific components
│       ├── stalls/                → Stall-related components
│       │   ├── StallTable.tsx
│       │   ├── StallCard.tsx
│       │   └── CreateStallModal.tsx
│       ├── payments/              → Payment-related components
│       │   ├── PaymentModal.tsx
│       │   └── PaymentHistoryModal.tsx
│       ├── vendors/               → Vendor-related components
│       └── dashboard/             → Dashboard-specific components
│
├── hooks/                         → Custom React hooks
│   ├── queries/                   → TanStack Query hooks (GET)
│   │   ├── useStalls.ts           → Fetch stalls
│   │   ├── usePayments.ts         → Fetch payments
│   │   ├── useDashboard.ts        → Fetch dashboard data
│   │   └── ...                    → One hook per query
│   │
│   └── mutations/                 → TanStack Query hooks (POST/PUT/DELETE)
│       ├── useCreateStall.ts      → Create stall mutation
│       ├── useRecordPayment.ts    → Record payment mutation
│       └── ...                    → One hook per mutation
│
├── pages/                         → Route components (one per route)
│   ├── Login.tsx                  → Login page
│   ├── Dashboard.tsx              → Dashboard overview
│   ├── Stalls.tsx                 → Stalls management
│   ├── Vendors.tsx                → Vendors management
│   ├── Payments.tsx               → Payments management
│   └── ...                        → One page per route
│
├── context/                       → React Context providers
│   └── AuthContext.tsx            → Auth state provider
│
├── types/                         → TypeScript type definitions
│   ├── dto.ts                     → DTOs matching backend
│   ├── enums.ts                   → Enums matching backend
│   └── api.ts                     → API response types
│
├── utils/                         → Utility functions
│   ├── cookieService.ts           → Token storage wrapper
│   ├── formatters.ts              → Date, currency formatters
│   └── constants.ts               → Frontend constants (FeeRates, DomainRules)
│
├── assets/                        → Static assets (images, icons)
│
├── App.tsx                        → Root component with providers
└── main.tsx                       → React entry point
```

---

## File Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| Component | PascalCase | `StallTable.tsx` |
| Hook | `use{Name}` | `useStalls.ts` |
| Service | `{entity}Service` | `stallService.ts` |
| Type/Interface | PascalCase | `StallDto` |
| Util | camelCase | `formatCurrency` |
| Constant | UPPER_SNAKE_CASE | `API_BASE_URL` |

---

## Key Principles

### API Layer (`api/`)
- **client.ts**: Axios instance with auth interceptors
- **endpoints.ts**: Centralized API endpoint constants
- **services/**: One service file per domain entity
  - Functions return `Promise<DTO>`
  - Never handle errors (let TanStack Query handle)

### Components (`components/`)
- **layout/**: App shell (Sidebar, Topbar, AdminLayout)
- **shared/**: Generic, reusable UI components (no domain knowledge)
- **features/**: Domain-specific components (receive data via props)

### Hooks (`hooks/`)
- **queries/**: GET operations using `useQuery`
- **mutations/**: POST/PUT/DELETE using `useMutation`
- Query keys: `['entity', ...filters]`
- Mutations invalidate related queries on success

### Pages (`pages/`)
- One component per route
- Fetch data using query hooks
- Compose feature components
- Handle loading/error states

### Types (`types/`)
- **dto.ts**: Copy exactly from backend C# DTOs
- **enums.ts**: Copy exactly from backend C# enums
- **api.ts**: API-specific types

### Utils (`utils/`)
- **cookieService.ts**: Token management
- **formatters.ts**: Date, currency formatting
- **constants.ts**: FeeRates, DomainRules (match backend)

---

## Data Flow

```
User Action
    ↓
Page Component
    ↓
Query/Mutation Hook (TanStack Query)
    ↓
API Service
    ↓
Axios Client (with interceptors)
    ↓
Backend API
```

---

## State Management

- **Server State**: TanStack Query (queries + mutations)
- **Auth State**: React Context (AuthContext)
- **UI State**: React useState/useReducer (local component state)
- **No Redux/Zustand needed**

---

## Styling

- **Tailwind CSS v4** utility classes only
- Design tokens in `tailwind.config.js`
- No CSS modules, no styled-components
- Mobile-first responsive design

---

## Environment Variables

Create `.env` file:

```bash
VITE_API_BASE_URL=http://localhost:5000/api
```

---

## Getting Started

```bash
# Install dependencies
npm install

# Install missing packages
npm install axios @tanstack/react-query react-router-dom js-cookie
npm install -D @types/js-cookie

# Run dev server
npm run dev

# Build for production
npm run build
```

---

## Next Steps

1. Install missing dependencies (axios, react-query, react-router-dom, js-cookie)
2. Configure Tailwind with design tokens
3. Create remaining service files (paymentService, dashboardService, etc.)
4. Create remaining shared components (Modal, Input, Select, DataTable, KpiCard)
5. Create layout components (AdminLayout, Sidebar, Topbar)
6. Create page components (Dashboard, Stalls, Vendors, etc.)
7. Set up routing in App.tsx
8. Connect to backend API

---

## Documentation

- **Architecture**: `.amazonq/rules/react-arch-rules.md`
- **Patterns**: `.amazonq/rules/react-patterns.md`
- **Styling**: `.amazonq/rules/react-styling.md`
- **Quick Ref**: `.amazonq/rules/react-quick-ref.md`
