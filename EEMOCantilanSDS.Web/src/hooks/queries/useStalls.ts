import { useQuery } from '@tanstack/react-query';
import { stallService } from '@/api/services/stallService';
import type { FacilityCode } from '@/types/enums';

export const useStall = (facilityCode: FacilityCode | string, stallNo: string) => {
  return useQuery({
    queryKey: ['stalls', facilityCode, stallNo],
    queryFn: async () => {
      const stalls = await stallService.getStallsByFacility(facilityCode as FacilityCode);
      const stall = stalls.find((s) => s.stallNo === stallNo);
      if (!stall) {
        throw new Error(`Stall ${stallNo} not found`);
      }
      return stall;
    },
    enabled: !!facilityCode && !!stallNo,
  });
};

export const useStalls = (facilityCode: FacilityCode) => {
  return useQuery({
    queryKey: ['stalls', facilityCode],
    queryFn: () => stallService.getStallsByFacility(facilityCode),
  });
};
