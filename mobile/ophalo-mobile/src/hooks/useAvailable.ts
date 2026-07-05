import { useQuery } from '@tanstack/react-query';

import { api, ApiError } from '../api/client';
import type { KeepRequestPageInfo, MyWorkResult } from './useMyWork';

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

function isOperator(role?: string | null): boolean {
  return role?.toLowerCase() === 'operator';
}

function toAvailableResult(result: MyWorkResult): AvailableRequestsResult {
  return {
    pageInfo: result.pageInfo,
    requests: result.requests.map((request) => ({
      requestId: request.id,
      referenceCode: request.referenceCode,
      customerName: request.customerName,
      status: request.status,
      createdAtUtc: request.createdAtUtc,
      attentionSinceUtc: request.attention.attentionSinceUtc,
      nextAttentionAtUtc: request.attention.nextAttentionAtUtc,
      priorityBand: request.attention.priorityBand,
      attentionLevel: request.attention.attentionLevel,
      descriptionPreview: request.preview.previewText ?? request.description,
      version: request.version,
      canSelfAssign: request.participation.canSelfAssignFromList === true,
      canWatch: false,
    })),
  };
}

export function useAvailableRequests(role?: string | null) {
  const operator = isOperator(role);

  return useQuery({
    queryKey: ['keepRequests', 'available', operator ? 'operator' : 'unassigned'],
    queryFn: async () => {
      if (operator) {
        return api.get<AvailableRequestsResult>('/keep/requests/available?limit=25');
      }

      const result = await api.get<MyWorkResult>('/keep/requests?view=unassigned');
      return toAvailableResult(result);
    },
    retry: (failureCount, error) => {
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
        return false;
      }
      return failureCount < 2;
    },
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}
