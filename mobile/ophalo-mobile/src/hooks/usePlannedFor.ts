import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type SetPlannedForVars = { requestId: string; version: string; date: string };
type ClearPlannedForVars = { requestId: string; version: string };

export function useSetPlannedFor() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, SetPlannedForVars>({
    mutationFn: ({ requestId, version, date }) =>
      api.put(
        `/keep/requests/${requestId}/planned-for`,
        { date },
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

export function useClearPlannedFor() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, ClearPlannedForVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete(`/keep/requests/${requestId}/planned-for`, { 'X-Keep-Request-Version': version }),
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
