import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type SetFollowUpVars = { requestId: string; version: string; date: string; reason?: string };
type ClearFollowUpVars = { requestId: string; version: string };

export function useSetFollowUpOn() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, SetFollowUpVars>({
    mutationFn: ({ requestId, version, date, reason }) =>
      api.put(
        `/keep/requests/${requestId}/follow-up-on`,
        { date, reason },
        { 'X-Keep-Request-Version': version },
      ),
    onSuccess: (_, { requestId }) => {
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
  return useMutation<unknown, Error, ClearFollowUpVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete(`/keep/requests/${requestId}/follow-up-on`, { 'X-Keep-Request-Version': version }),
    onSuccess: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
      queryClient.invalidateQueries({ queryKey: ['keepRequests'] });
      queryClient.invalidateQueries({ queryKey: ['badge'] });
    },
    onError: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
    },
  });
}
