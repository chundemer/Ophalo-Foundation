import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type LogContactVars = {
  requestId: string;
  version: string;
  direction: string;
  channel: string;
  outcome?: string;
};

export function useLogExternalContact() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, LogContactVars>({
    mutationFn: ({ requestId, version, direction, channel, outcome }) =>
      api.post(
        `/keep/requests/${requestId}/external-contact`,
        { direction, channel, outcome },
        true,
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
