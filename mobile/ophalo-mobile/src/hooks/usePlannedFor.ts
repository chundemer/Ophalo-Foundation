import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';
import type { KeepRequestDetailDto } from './useRequestDetail';

type SetPlannedForVars = { requestId: string; version: string; date: string };
type ClearPlannedForVars = { requestId: string; version: string };

export function useSetPlannedFor() {
  const queryClient = useQueryClient();
  return useMutation<KeepRequestDetailDto, Error, SetPlannedForVars>({
    mutationFn: ({ requestId, version, date }) =>
      api.put<KeepRequestDetailDto>(
        `/keep/requests/${requestId}/planned-for`,
        { date },
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

export function useClearPlannedFor() {
  const queryClient = useQueryClient();
  return useMutation<KeepRequestDetailDto, Error, ClearPlannedForVars>({
    mutationFn: ({ requestId, version }) =>
      api.delete<KeepRequestDetailDto>(
        `/keep/requests/${requestId}/planned-for`,
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
