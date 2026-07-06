import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';
import type { KeepRequestDetailDto } from './useRequestDetail';

type SetFollowUpVars = { requestId: string; version: string; date: string; reason?: string };
type ClearFollowUpVars = { requestId: string; version: string };

export function useSetFollowUpOn() {
  const queryClient = useQueryClient();
  return useMutation<KeepRequestDetailDto, Error, SetFollowUpVars>({
    mutationFn: ({ requestId, version, date, reason }) =>
      api.put<KeepRequestDetailDto>(
        `/keep/requests/${requestId}/follow-up-on`,
        { date, reason },
        { 'X-Keep-Request-Version': version },
      ),
    onSuccess: (detail, { requestId }) => {
      queryClient.setQueryData(['keepRequestDetail', requestId], detail);
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
      queryClient.invalidateQueries({ queryKey: ['keepRequests'] });
      queryClient.invalidateQueries({ queryKey: ['badge'] });
    },
    onError: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
    },
  });
}

export function useClearFollowUpOn() {
  const queryClient = useQueryClient();
  return useMutation<KeepRequestDetailDto, Error, ClearFollowUpVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete<KeepRequestDetailDto>(
        `/keep/requests/${requestId}/follow-up-on`,
        { 'X-Keep-Request-Version': version },
      ),
    onSuccess: (detail, { requestId }) => {
      queryClient.setQueryData(['keepRequestDetail', requestId], detail);
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
      queryClient.invalidateQueries({ queryKey: ['keepRequests'] });
      queryClient.invalidateQueries({ queryKey: ['badge'] });
    },
    onError: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
    },
  });
}
