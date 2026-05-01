import { apiClient } from '../client';
import { ENDPOINTS } from '../endpoints';
import type { LoginCommand, LoginResponse, AdminUserDto, SetupStatusDto, CreateFirstAdminCommand } from '@/types/dto';

export const authService = {
  login: async (command: LoginCommand): Promise<LoginResponse> => {
    const { data } = await apiClient.post<LoginResponse>(ENDPOINTS.AUTH.LOGIN, command);
    return data;
  },

  logout: async (): Promise<void> => {
    await apiClient.post(ENDPOINTS.AUTH.LOGOUT);
  },

  getCurrentUser: async (): Promise<AdminUserDto> => {
    const { data } = await apiClient.get<AdminUserDto>(ENDPOINTS.AUTH.CURRENT_USER);
    return data;
  },

  getSetupStatus: async (): Promise<SetupStatusDto> => {
    const { data } = await apiClient.get<SetupStatusDto>(ENDPOINTS.SETUP.STATUS);
    return data;
  },

  createFirstAdmin: async (command: CreateFirstAdminCommand): Promise<void> => {
    await apiClient.post(ENDPOINTS.SETUP.CREATE_FIRST_ADMIN, command);
  },
};
