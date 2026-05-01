import { apiClient } from '../client';
import { ENDPOINTS } from '../endpoints';
import type { CollectorListDto, CollectorActivityDto, CreateCollectorCommand, CollectorDto } from '@/types/dto';

export const collectorService = {
  getAll: async (): Promise<CollectorListDto[]> => {
    const { data } = await apiClient.get<CollectorListDto[]>(ENDPOINTS.COLLECTORS.BASE);
    return data;
  },

  getById: async (id: string): Promise<CollectorActivityDto> => {
    const { data } = await apiClient.get<CollectorActivityDto>(ENDPOINTS.COLLECTORS.BY_ID(id));
    return data;
  },

  create: async (command: CreateCollectorCommand): Promise<CollectorDto> => {
    const { data } = await apiClient.post<CollectorDto>(ENDPOINTS.COLLECTORS.BASE, command);
    return data;
  },
};
