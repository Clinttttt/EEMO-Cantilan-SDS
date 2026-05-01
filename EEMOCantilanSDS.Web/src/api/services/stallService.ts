import { apiClient } from '../client';
import { ENDPOINTS } from '../endpoints';
import type { StallDto, CreateStallCommand, UpdateStallCommand } from '@/types/dto';
import { FacilityCode } from '@/types/enums';

export const stallService = {
  getStallsByFacility: async (facilityCode: FacilityCode): Promise<StallDto[]> => {
    const { data } = await apiClient.get<{ items: StallDto[]; nextCursor: string | null; hasMore: boolean }>(
      ENDPOINTS.STALLS.BY_FACILITY_PAGINATED(facilityCode, undefined, undefined, 100)
    );
    return data.items;
  },

  getStallById: async (id: string): Promise<StallDto> => {
    const { data } = await apiClient.get<StallDto>(ENDPOINTS.STALLS.BY_ID(id));
    return data;
  },

  createStall: async (command: CreateStallCommand): Promise<StallDto> => {
    const { data } = await apiClient.post<StallDto>(ENDPOINTS.STALLS.BASE, command);
    return data;
  },

  updateStall: async (id: string, command: UpdateStallCommand): Promise<StallDto> => {
    const { data } = await apiClient.put<StallDto>(ENDPOINTS.STALLS.BY_ID(id), command);
    return data;
  },

  deleteStall: async (id: string): Promise<void> => {
    await apiClient.delete(ENDPOINTS.STALLS.BY_ID(id));
  },

  toggleStallStatus: async (id: string): Promise<StallDto> => {
    const { data } = await apiClient.patch<StallDto>(ENDPOINTS.STALLS.TOGGLE_STATUS(id));
    return data;
  },
};
