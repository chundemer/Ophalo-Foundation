import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type SendBusinessUpdateVars = {
  requestId: string;
  version: string;
  message: string;
  setStatus?: string;
};

export function useSendBusinessUpdate() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, SendBusinessUpdateVars>({
    mutationFn: ({ requestId, version, message, setStatus }) =>
      api.post(
        `/keep/requests/${requestId}/business-updates`,
        { message, setStatus },
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
