import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type WatchVars = { requestId: string; version: string };

export function useWatchRequest() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, WatchVars>({
    mutationFn: ({ requestId, version }) =>
      api.put(`/keep/requests/${requestId}/watch`, undefined, { 'X-Keep-Request-Version': version }),
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

export function useUnwatchRequest() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, WatchVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete(`/keep/requests/${requestId}/watch`, { 'X-Keep-Request-Version': version }),
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
