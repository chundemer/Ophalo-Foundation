import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type MuteVars = { requestId: string; version: string };

export function useMuteRequest() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, MuteVars>({
    mutationFn: ({ requestId, version }) =>
      api.put(`/keep/requests/${requestId}/mute`, undefined, { 'X-Keep-Request-Version': version }),
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

export function useUnmuteRequest() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, MuteVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete(`/keep/requests/${requestId}/mute`, { 'X-Keep-Request-Version': version }),
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
