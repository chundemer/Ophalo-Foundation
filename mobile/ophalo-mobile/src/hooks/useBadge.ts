import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';

type BadgeResult = {
  count: number;
  computedAtUtc: string;
};

export function useBadge() {
  return useQuery({
    queryKey: ['badge'],
    queryFn: () => api.get<BadgeResult>('/me/badge'),
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}
