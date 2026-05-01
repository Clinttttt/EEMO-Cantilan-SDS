import { useQuery } from '@tanstack/react-query';
import { authService } from '@/api/services/authService';

export const useSetupStatus = () => {
  return useQuery({
    queryKey: ['setup-status'],
    queryFn: authService.getSetupStatus,
  });
};
