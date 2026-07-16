import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';

export interface PhoneLookupCustomer {
  name: string;
  phone: string;
  email?: string | null;
}

export interface PhoneLookupActiveRequest {
  requestId: string;
  referenceCode: string;
  status: string;
  description: string;
  lastActivityAtUtc?: string | null;
}

export interface PhoneLookupResult {
  customer: PhoneLookupCustomer | null;
  activeRequests: PhoneLookupActiveRequest[];
  hasMoreActiveRequests: boolean;
}

export interface CreateRequestBody {
  customerName: string;
  customerPhone: string;
  customerEmail?: string | null;
  description: string;
  source: string;
  serviceAddressLine1?: string;
  serviceAddressLine2?: string;
  serviceCity?: string;
  serviceState?: string;
  serviceZip?: string;
}

export interface CreatedRequestResult {
  requestId: string;
}

import { normalizePhoneDigits, validateAddressIfOpen } from './phoneUtils';
export type { AddressErrors } from './phoneUtils';
export { normalizePhoneDigits, validateAddressIfOpen };

export function usePhoneLookup(rawPhone: string) {
  const digits = normalizePhoneDigits(rawPhone);
  const enabled = digits.length === 10;

  return useQuery<PhoneLookupResult>({
    queryKey: ['phoneLookup', digits],
    queryFn: () => api.get<PhoneLookupResult>(`/keep/requests/lookup?phone=${encodeURIComponent(digits)}`),
    enabled,
    staleTime: 30_000,
  });
}

export function useCreateRequest() {
  const queryClient = useQueryClient();

  return useMutation<CreatedRequestResult, Error, CreateRequestBody>({
    mutationFn: (body) =>
      api.post<CreatedRequestResult>('/keep/requests', body, true),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['keepRequests'] });
      queryClient.invalidateQueries({ queryKey: ['badge'] });
    },
  });
}
