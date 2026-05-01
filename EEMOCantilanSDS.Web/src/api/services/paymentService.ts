import { apiClient } from '../client';
import { ENDPOINTS } from '../endpoints';
import type { PaymentRecordDto, RecordPaymentCommand } from '@/types/dto';

export const paymentService = {
  getPaymentHistory: async (stallId: string): Promise<PaymentRecordDto[]> => {
    const { data } = await apiClient.get<PaymentRecordDto[]>(ENDPOINTS.PAYMENTS.HISTORY(stallId));
    return data;
  },

  getPaymentRecord: async (stallId: string, year: number, month: number): Promise<PaymentRecordDto> => {
    const { data } = await apiClient.get<PaymentRecordDto>(ENDPOINTS.PAYMENTS.RECORD(stallId, year, month));
    return data;
  },

  recordPayment: async (command: RecordPaymentCommand): Promise<PaymentRecordDto> => {
    const { data } = await apiClient.post<PaymentRecordDto>(ENDPOINTS.PAYMENTS.RECORD_PAYMENT, command);
    return data;
  },
};
