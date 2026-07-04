import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type AssignVars = { requestId: string; version: string; accountUserId: string };

export function useAssignResponsible() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, AssignVars>({
    mutationFn: ({ requestId, version, accountUserId }) =>
      api.put(
        `/keep/requests/${requestId}/responsible`,
        { accountUserId },
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
