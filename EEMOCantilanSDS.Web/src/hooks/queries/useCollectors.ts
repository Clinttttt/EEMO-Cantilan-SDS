import { useQuery } from '@tanstack/react-query';
import { collectorService } from '@/api/services/collectorService';

export const useCollectors = () => {
  return useQuery({
    queryKey: ['collectors'],
    queryFn: collectorService.getAll,
    staleTime: 5 * 60 * 1000,
  });
};

export const useCollectorActivity = (id: string) => {
  return useQuery({
    queryKey: ['collectors', id, 'activity'],
    queryFn: () => collectorService.getById(id),
    enabled: !!id,
  });
};
