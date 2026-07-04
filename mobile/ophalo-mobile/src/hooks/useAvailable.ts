import { useQuery } from '@tanstack/react-query';

import { api } from '../api/client';
import type { KeepRequestPageInfo } from './useMyWork';

export type AvailableRequestItem = {
  requestId: string;
  referenceCode: string;
  customerName: string;
  status: string;
  createdAtUtc: string;
  attentionSinceUtc?: string | null;
  nextAttentionAtUtc?: string | null;
  priorityBand: string;
  attentionLevel: string;
  descriptionPreview: string;
  version: string;
  canSelfAssign: boolean;
  canWatch: boolean;
};

export type AvailableRequestsResult = {
  requests: AvailableRequestItem[];
  pageInfo: KeepRequestPageInfo;
};

export function useAvailableRequests() {
  return useQuery({
    queryKey: ['keepRequests', 'available'],
    queryFn: () => api.get<AvailableRequestsResult>('/keep/requests/available?limit=25'),
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}
