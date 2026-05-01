# EEMOCantilanSDS.Web — React Frontend

**Tech Stack:** React 19 + TypeScript + Vite 8 + TanStack Query v5 + Tailwind CSS v4

---

## 📁 Current Folder Structure

```
src/
├── api/                           ✅ CREATED
│   ├── client.ts                  ✅ Axios instance with auth interceptors
│   ├── endpoints.ts               ✅ API endpoint constants
│   └── services/                  ✅ API service layer
│       ├── authService.ts         ✅ Login, logout, user management
│       └── stallService.ts        ✅ Stall CRUD operations
│
├── components/
│   ├── layout/                    ⚠️ PARTIAL
│   │   └── ProtectedRoute.tsx    ✅ Auth guard
│   ├── shared/                    ⚠️ PARTIAL
│   │   ├── Button.tsx             ✅ Button with variants
│   │   └── Spinner.tsx            ✅ Loading spinner
│   └── features/                  📁 Empty folders ready
│       ├── stalls/
│       ├── payments/
│       ├── vendors/
│       └── dashboard/
│
├── hooks/                         📁 Empty folders ready
│   ├── queries/
│   └── mutations/
│
├── pages/                         📁 Empty folder ready
│
├── context/                       ✅ CREATED
│   └── AuthContext.tsx            ✅ Auth state provider
│
├── types/                         ✅ CREATED
│   ├── dto.ts                     ✅ DTOs matching backend
│   └── enums.ts                   ✅ Enums matching backend
│
└── utils/                         ✅ CREATED
    ├── constants.ts               ✅ FeeRates, DomainRules
    ├── cookieService.ts           ✅ Token management
    └── formatters.ts              ✅ Date, currency formatters
```

---

## ✅ What's Been Created

### Core Infrastructure
- ✅ **API Client** (`api/client.ts`) — Axios with auth interceptors
- ✅ **API Endpoints** (`api/endpoints.ts`) — Centralized endpoint constants
- ✅ **Auth Service** (`api/services/authService.ts`) — Login, logout, user management
- ✅ **Stall Service** (`api/services/stallService.ts`) — Stall CRUD operations

### Type System
- ✅ **Enums** (`types/enums.ts`) — FacilityCode, PaymentStatus, AdminRole, etc.
- ✅ **DTOs** (`types/dto.ts`) — All DTOs matching backend exactly

### Utilities
- ✅ **Constants** (`utils/constants.ts`) — FeeRates, DomainRules, FacilityNames
- ✅ **Cookie Service** (`utils/cookieService.ts`) — Token storage wrapper
- ✅ **Formatters** (`utils/formatters.ts`) — Currency, date formatting

### Auth System
- ✅ **Auth Context** (`context/AuthContext.tsx`) — Global auth state
- ✅ **Protected Route** (`components/layout/ProtectedRoute.tsx`) — Auth guard

### Shared Components
- ✅ **Button** (`components/shared/Button.tsx`) — Button with variants
- ✅ **Spinner** (`components/shared/Spinner.tsx`) — Loading spinner

---

## 📦 Required Dependencies

Install these packages:

```bash
npm install axios @tanstack/react-query react-router-dom js-cookie
npm install -D @types/js-cookie
```

---

## 🚀 Next Steps

### 1. Complete Shared Components
Create in `components/shared/`:
- [ ] `Modal.tsx` — Reusable modal wrapper
- [ ] `Input.tsx` — Form input with error display
- [ ] `Select.tsx` — Dropdown with error display
- [ ] `DataTable.tsx` — Generic data table
- [ ] `KpiCard.tsx` — Dashboard KPI card
- [ ] `EmptyState.tsx` — Empty state message

### 2. Create Layout Components
Create in `components/layout/`:
- [ ] `AdminLayout.tsx` — Main app shell
- [ ] `Sidebar.tsx` — Navigation sidebar
- [ ] `Topbar.tsx` — Top navigation bar

### 3. Create API Services
Create in `api/services/`:
- [ ] `paymentService.ts` — Payment operations
- [ ] `dashboardService.ts` — Dashboard data
- [ ] `contractService.ts` — Contract operations
- [ ] `dailyCollectionService.ts` — NPM daily collections
- [ ] `slaughterhouseService.ts` — Slaughterhouse transactions

### 4. Create Query Hooks
Create in `hooks/queries/`:
- [ ] `useStalls.ts` — Fetch stalls by facility
- [ ] `usePayments.ts` — Fetch payment history
- [ ] `useDashboard.ts` — Fetch dashboard overview
- [ ] `useContracts.ts` — Fetch contracts

### 5. Create Mutation Hooks
Create in `hooks/mutations/`:
- [ ] `useCreateStall.ts` — Create stall
- [ ] `useUpdateStall.ts` — Update stall
- [ ] `useRecordPayment.ts` — Record payment
- [ ] `useSaveORNumber.ts` — Save OR number

### 6. Create Pages
Create in `pages/`:
- [ ] `Login.tsx` — Login page
- [ ] `Dashboard.tsx` — Dashboard overview
- [ ] `Stalls.tsx` — Stalls management
- [ ] `Vendors.tsx` — Vendors management
- [ ] `Payments.tsx` — Payments management
- [ ] `Reports.tsx` — Reports page

### 7. Configure Tailwind
Update `tailwind.config.js` with design tokens:

```javascript
export default {
  theme: {
    extend: {
      colors: {
        navy: { DEFAULT: '#0d2137', 2: '#112d47', 3: '#1e3a5f' },
        gold: { DEFAULT: '#c8a84b', light: '#e8cc76' },
        green: { DEFAULT: '#2d7a5f', bg: '#e6f4ef' },
        red: { DEFAULT: '#8b3a3a', bg: '#fdf0f0' },
        bg: { DEFAULT: '#f0f4f8', card: '#ffffff', icon: '#eef2f6' },
        border: '#dde4ea',
        text: { DEFAULT: '#0d2137', muted: '#8faabf', subtle: '#6a8aa0' },
      },
      spacing: {
        sidebar: '240px',
        topbar: '64px',
      },
    },
  },
};
```

### 8. Set Up Routing
Update `App.tsx` with React Router and TanStack Query:

```typescript
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './context/AuthContext';
import { ProtectedRoute } from './components/layout/ProtectedRoute';
import { AdminLayout } from './components/layout/AdminLayout';
import { Login } from './pages/Login';
import { Dashboard } from './pages/Dashboard';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
      staleTime: 5 * 60 * 1000,
    },
  },
});

export const App = () => {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route
              path="/*"
              element={
                <ProtectedRoute>
                  <AdminLayout>
                    <Routes>
                      <Route path="/" element={<Navigate to="/dashboard" replace />} />
                      <Route path="/dashboard" element={<Dashboard />} />
                      {/* Add more routes */}
                    </Routes>
                  </AdminLayout>
                </ProtectedRoute>
              }
            />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
};
```

### 9. Environment Variables
Create `.env` file:

```bash
VITE_API_BASE_URL=http://localhost:5000/api
```

---

## 📚 Documentation

Full documentation available in `.amazonq/rules/`:

- **react-arch-rules.md** — Architecture and layer responsibilities
- **react-patterns.md** — Implementation patterns and examples
- **react-styling.md** — Tailwind CSS design system
- **react-quick-ref.md** — Quick reference cheat sheet
- **FOLDER_STRUCTURE.md** — Detailed folder structure guide

---

## 🎯 Key Patterns

### API Service Pattern
```typescript
// api/services/stallService.ts
export const stallService = {
  getStallsByFacility: async (facilityCode: FacilityCode): Promise<StallDto[]> => {
    const { data } = await apiClient.get<StallDto[]>(`/stalls/facility/${facilityCode}`);
    return data;
  },
};
```

### Query Hook Pattern
```typescript
// hooks/queries/useStalls.ts
export const useStalls = (facilityCode: FacilityCode) => {
  return useQuery({
    queryKey: ['stalls', facilityCode],
    queryFn: () => stallService.getStallsByFacility(facilityCode),
  });
};
```

### Mutation Hook Pattern
```typescript
// hooks/mutations/useCreateStall.ts
export const useCreateStall = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: stallService.createStall,
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['stalls', data.facilityCode] });
    },
  });
};
```

### Page Component Pattern
```typescript
// pages/Stalls.tsx
export const Stalls = () => {
  const { data, isLoading, error } = useStalls(FacilityCode.NPM);
  
  if (isLoading) return <Spinner />;
  if (error) return <div>Error: {error.message}</div>;
  
  return (
    <div className="p-6">
      <h1 className="text-2xl font-bold text-navy">Stalls</h1>
      <StallTable stalls={data} />
    </div>
  );
};
```

---

## 🔒 Auth Flow

1. User logs in → access token stored in cookie (15min)
2. Refresh token stored in httpOnly cookie (7 days)
3. Axios interceptor adds access token to requests
4. On 401 → auto refresh → retry original request
5. On refresh failure → clear auth → redirect to login

---

## 🎨 Design Tokens

All design tokens match the backend Blazor design system:

- **Navy** (#0d2137) — Primary text/nav
- **Gold** (#c8a84b) — Accent color
- **Green** (#2d7a5f) — Success states
- **Red** (#8b3a3a) — Error states
- **Background** (#f0f4f8) — Page background
- **Card** (#ffffff) — Card background

---

## ✨ Features

- ✅ Type-safe API communication
- ✅ Automatic token refresh
- ✅ Protected routes with auth guard
- ✅ TanStack Query for server state
- ✅ Tailwind CSS for styling
- ✅ React Router for navigation
- ✅ Constants matching backend exactly

---

## 🚦 Getting Started

```bash
# Install dependencies
npm install

# Install required packages
npm install axios @tanstack/react-query react-router-dom js-cookie
npm install -D @types/js-cookie

# Run dev server
npm run dev

# Build for production
npm run build
```

---

## 📝 Notes

- All DTOs and enums match backend exactly
- All constants (FeeRates, DomainRules) match backend
- No business logic in frontend — API handles everything
- TanStack Query handles all server state
- React Context only for auth state
- Tailwind CSS only — no CSS modules or styled-components
