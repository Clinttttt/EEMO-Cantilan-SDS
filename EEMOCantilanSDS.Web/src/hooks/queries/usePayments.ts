import { useQuery } from '@tanstack/react-query';
import { paymentService } from '@/api/services/paymentService';

export const usePaymentHistory = (stallId?: string) => {
  return useQuery({
    queryKey: ['payments', 'history', stallId],
    queryFn: () => paymentService.getPaymentHistory(stallId!),
    enabled: !!stallId,
  });
};

export const usePaymentRecord = (stallId: string, year: number, month: number) => {
  return useQuery({
    queryKey: ['payments', stallId, year, month],
    queryFn: () => paymentService.getPaymentRecord(stallId, year, month),
    enabled: !!stallId,
  });
};
