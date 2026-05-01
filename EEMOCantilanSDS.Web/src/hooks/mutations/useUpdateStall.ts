import { useMutation, useQueryClient } from '@tanstack/react-query';
import { stallService } from '@/api/services/stallService';
import { paymentService } from '@/api/services/paymentService';
import type { UpdateStallCommand, RecordPaymentCommand } from '@/types/dto';

export const useUpdateStall = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: UpdateStallCommand) => stallService.updateStall(command.stallId, command),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['stalls'] });
      queryClient.invalidateQueries({ queryKey: ['stalls', data.facilityCode] });
    },
  });
};

export const useRecordPayment = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: RecordPaymentCommand) => paymentService.recordPayment(command),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['payments'] });
    },
  });
};
