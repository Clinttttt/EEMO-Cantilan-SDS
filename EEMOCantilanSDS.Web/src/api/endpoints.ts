// API endpoint constants
export const ENDPOINTS = {
  // Auth
  AUTH: {
    LOGIN: '/adminauth/login',
    REFRESH: '/adminauth/refresh-token',
    LOGOUT: '/adminauth/logout',
    CURRENT_USER: '/adminauth/current-user',
  },

  // Setup
  SETUP: {
    STATUS: '/setup/status',
    CREATE_FIRST_ADMIN: '/setup/create-first-admin',
  },

  // Collectors
  COLLECTORS: {
    BASE: '/collectors',
    BY_ID: (id: string) => `/collectors/${id}`,
  },

  // Stalls
  STALLS: {
    BASE: '/stalls',
    BY_FACILITY_PAGINATED: (facilityCode: number, section?: number, cursor?: string, pageSize: number = 20) => {
      let url = `/stalls/facility/${facilityCode}/paginated?pageSize=${pageSize}`;
      if (section !== undefined) url += `&section=${section}`;
      if (cursor) url += `&cursor=${cursor}`;
      return url;
    },
    BY_ID: (id: string) => `/stalls/${id}`,
    TOGGLE_STATUS: (id: string) => `/stalls/${id}/toggle-status`,
  },

  // Payments
  PAYMENTS: {
    BASE: '/payments',
    HISTORY: (stallId: string) => `/payments/history/${stallId}`,
    RECORD: (stallId: string, year: number, month: number) => 
      `/payments/${stallId}/${year}/${month}`,
    RECORD_PAYMENT: '/payments/record',
    UPDATE_STATUS: '/payments/update-status',
    SAVE_OR_NUMBER: '/payments/save-or-number',
  },

  // Dashboard
  DASHBOARD: {
    OVERVIEW: (year: number, month: number) => `/dashboard/overview?year=${year}&month=${month}`,
  },

  // Vendors
  VENDORS: {
    BASE: '/vendors',
    BY_ID: (id: string) => `/vendors/${id}`,
  },

  // Contracts
  CONTRACTS: {
    BASE: '/contracts',
    BY_STALL: (stallId: string) => `/contracts/stall/${stallId}`,
  },

  // Daily Collections (NPM)
  DAILY_COLLECTIONS: {
    BASE: '/daily-collections',
    BY_STALL: (stallId: string, date: string) => 
      `/daily-collections/stall/${stallId}?date=${date}`,
  },

  // Slaughterhouse
  SLAUGHTERHOUSE: {
    BASE: '/slaughterhouse',
    TRANSACTIONS: '/slaughterhouse/transactions',
  },

  // Reports
  REPORTS: {
    FACILITY_SUMMARY: (facilityCode: number, year: number, month: number) => 
      `/reports/facility-summary/${facilityCode}?year=${year}&month=${month}`,
    DELINQUENT_VENDORS: '/reports/delinquent-vendors',
  },
} as const;
