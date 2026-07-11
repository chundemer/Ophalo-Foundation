import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';
import { KeepRequestDetailDto } from './useRequestDetail';

type PatchStatusVars = {
  requestId: string;
  version: string;
  status: string;
};

export function usePatchRequestStatus() {
  const queryClient = useQueryClient();
  return useMutation<KeepRequestDetailDto, Error, PatchStatusVars>({
    mutationFn: ({ requestId, version, status }) =>
      api.patch<KeepRequestDetailDto>(
        `/keep/requests/${requestId}/status`,
        { status },
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
