import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '../api/client';

type AddInternalNoteVars = {
  requestId: string;
  version: string;
  note: string;
};

export function useAddInternalNote() {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, AddInternalNoteVars>({
    mutationFn: ({ requestId, version, note }) =>
      api.post(
        `/keep/requests/${requestId}/internal-notes`,
        { note },
        true,
        { 'X-Keep-Request-Version': version },
      ),
    onSuccess: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
      queryClient.invalidateQueries({ queryKey: ['keepRequests'] });
    },
    onError: (_, { requestId }) => {
      queryClient.invalidateQueries({ queryKey: ['keepRequestDetail', requestId] });
    },
  });
}
