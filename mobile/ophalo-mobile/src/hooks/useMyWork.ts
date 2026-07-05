import { useQuery } from '@tanstack/react-query';

import { api } from '../api/client';

export type MyWorkView = 'assigned_to_me' | 'watching' | 'unassigned';

export type KeepRequestSummary = {
  id: string;
  referenceCode: string;
  status: string;
  currentStatusText?: string | null;
  customerName: string;
  description: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: string;
  needsShare: boolean;
  attention: {
    attentionLevel: string;
    priorityBand: string;
    attentionReason?: string | null;
    attentionSinceUtc?: string | null;
    nextAttentionAtUtc?: string | null;
  };
  preview: {
    previewText?: string | null;
    previewSource?: string | null;
    previewTruncated: boolean;
  };
  participation: {
    responsibleDisplayName?: string | null;
    currentUserParticipationType: string;
    canSelfAssignFromList?: boolean;
  };
  timing: {
    followUpOnLabel?: string | null;
    plannedForLabel?: string | null;
  };
};

export type KeepRequestPageInfo = {
  limit: number;
  hasMore: boolean;
  nextCursor?: string | null;
};

export type MyWorkResult = {
  requests: KeepRequestSummary[];
  pageInfo: KeepRequestPageInfo;
};

export function useMyWork(view: MyWorkView) {
  return useQuery({
    queryKey: ['keepRequests', view],
    queryFn: () => api.get<MyWorkResult>(`/keep/requests?view=${view}`),
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}
