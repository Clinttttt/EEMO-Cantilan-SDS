// DTOs matching backend C# DTOs exactly
import { FacilityCode, PaymentStatus, AdminRole, AnimalType, NPMSection, NCCAreaLocation } from './enums';

// Auth DTOs
export interface LoginCommand {
  username: string;
  password: string;
}

export interface LoginResponse extends AdminUserDto {
  // Tokens are now in HttpOnly cookies, not in response
}

export interface AdminUserDto {
  id: string;
  fullName: string;
  username: string;
  email: string;
  adminRole: AdminRole;
  isActive: boolean;
  mustChangePassword: boolean;
}

export interface SetupStatusDto {
  isSetupRequired: boolean;
}

export interface CreateFirstAdminCommand {
  fullName: string;
  username: string;
  email: string;
  password: string;
}

// Collector DTOs
export interface CollectorListDto {
  id: string;
  fullName: string;
  email: string;
  employeeId: string;
  assignedFacilities: FacilityCode[];
  collectedThisMonth: number;
  transactions: number;
  lastActiveAt?: string;
  isActive: boolean;
}

export interface CollectorActivityDto {
  id: string;
  fullName: string;
  employeeId: string;
  email: string;
  contactNumber: string;
  assignedFacilities: FacilityCode[];
  collectedThisMonth: number;
  transactions: number;
  facilitiesCount: number;
  lastActiveAt?: string;
  recentTransactions: RecentTransactionDto[];
}

export interface RecentTransactionDto {
  orNumber: string;
  payorName: string;
  facility: FacilityCode;
  nature: string;
  amount: number;
  status: string;
  transactionDate: string;
}

export interface CollectorDto {
  id: string;
  fullName: string;
  employeeId: string;
  username: string;
  email: string;
  contactNumber: string;
  isActive: boolean;
  assignedFacilities: FacilityCode[];
}

export interface CreateCollectorCommand {
  fullName: string;
  employeeId: string;
  contactNumber: string;
  email: string;
  username: string;
  password: string;
  assignedFacilities: FacilityCode[];
}

// Stall DTOs
export interface StallDto {
  id: string;
  facilityCode: FacilityCode;
  stallNo: string;
  monthlyRate: number;
  dailyRate?: number;
  section?: string;
  areaLocation?: string;
  areaSqm?: number;
  actualOccupant?: string;
  nameOnContract?: string;
  contractDate?: string;
  remarks?: string;
  collectorName?: string;
  isActive: boolean;
}

export interface CreateStallCommand {
  facilityCode: FacilityCode;
  stallNo: string;
  monthlyRate: number;
  dailyRate?: number;
  section?: string;
  areaLocation?: string;
}

export interface UpdateStallCommand {
  stallId: string;
  monthlyRate: number;
  areaSqm?: number;
  areaLocation?: string;
  actualOccupant?: string;
  nameOnContract?: string;
  remarks?: string;
}

// Contract DTOs
export interface ContractDto {
  id: string;
  stallId: string;
  actualOccupant: string;
  nameOnContract: string;
  startDate: string;
  endDate: string;
  isExpired: boolean;
  isExpiringSoon: boolean;
}

export interface CreateContractCommand {
  stallId: string;
  actualOccupant: string;
  nameOnContract: string;
  startDate: string;
  endDate: string;
}

// Payment DTOs
export interface PaymentRecordDto {
  id: string;
  stallId: string;
  billingYear: number;
  billingMonth: number;
  orNumber?: string;
  status: PaymentStatus;
  utilitiesAmount: number;
  fishKilos?: number;
  totalBill: number;
  balanceDue: number;
  amountPaid: number;
  fishFeeAmount?: number;
}

export interface RecordPaymentCommand {
  stallId: string;
  year: number;
  month: number;
  status: PaymentStatus;
  partialAmount?: number;
  remarks?: string;
}

export interface SaveORNumberCommand {
  paymentRecordId: string;
  orNumber: string;
}

// Daily Collection DTOs (NPM)
export interface DailyCollectionDto {
  id: string;
  stallId: string;
  collectionDate: string;
  orNumber?: string;
  fishKilos?: number;
  totalCollected: number;
  fishFeeAmount?: number;
}

export interface RecordDailyCollectionCommand {
  stallId: string;
  collectionDate: string;
  orNumber?: string;
  fishKilos?: number;
}

// Dashboard DTOs
export interface DashboardOverviewDto {
  totalStalls: number;
  activeContracts: number;
  monthlyRevenue: number;
  delinquentCount: number;
  facilitySummaries: FacilitySummaryDto[];
}

export interface FacilitySummaryDto {
  facilityCode: FacilityCode;
  facilityName: string;
  totalStalls: number;
  activeStalls: number;
  totalRevenue: number;
}

// Slaughterhouse DTOs
export interface SlaughterTransactionDto {
  id: string;
  animalType: AnimalType;
  headCount: number;
  totalAmount: number;
  transactionDate: string;
  orNumber?: string;
}

export interface RecordSlaughterCommand {
  animalType: AnimalType;
  headCount: number;
  orNumber?: string;
}
