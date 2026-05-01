import { useMutation } from '@tanstack/react-query';
import { authService } from '@/api/services/authService';
import type { CreateFirstAdminCommand } from '@/types/dto';

export const useCreateFirstAdmin = () => {
  return useMutation({
    mutationFn: (command: CreateFirstAdminCommand) => authService.createFirstAdmin(command),
  });
};
