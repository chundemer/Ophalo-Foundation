import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type ShareIntentVars = {
  requestId: string;
  method: 'copy_link' | 'native_share' | 'manual_mark_shared';
};

export function useClearShareIntent() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, ShareIntentVars>({
    mutationFn: ({ requestId, method }) =>
      api.post(`/keep/requests/${requestId}/share-intent`, { method }, true),
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
