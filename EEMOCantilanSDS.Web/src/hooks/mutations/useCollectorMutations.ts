import { useMutation, useQueryClient } from '@tanstack/react-query';
import { collectorService } from '@/api/services/collectorService';
import type { CreateCollectorCommand } from '@/types/dto';

export const useCreateCollector = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: CreateCollectorCommand) => collectorService.create(command),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['collectors'] });
    },
  });
};
