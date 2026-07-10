import { useQuery } from '@tanstack/react-query';

import { api } from '../api/client';

export type ContactActionItem = {
  type: string;
  available: boolean;
  target: string;
};

export type ParticipantItem = {
  accountUserId: string;
  displayName: string;
  role: string;
  participationType: string;
  notificationsEnabled: boolean;
  detachedAtUtc: string | null;
};

export type CurrentUserParticipationDto = {
  participationType: string;
  notificationsEnabled: boolean | null;
};

export type EventItem = {
  id: string;
  eventType: string;
  content: string | null;
  visibility: string;
  occurredAtUtc: string;
  actorType: string;
  actorDisplayName: string | null;
  statusAfter: string | null;
  messageIntent: string | null;
  communicationChannel: string | null;
  externalContactDirection: string | null;
  externalContactChannel: string | null;
  externalContactOutcome: string | null;
  plannedForDate: string | null;
  followUpOnDate: string | null;
  followUpOnReason: string | null;
};

export type AvailableActionsDto = {
  canChangeStatus: boolean;
  canSendBusinessUpdate: boolean;
  canAddInternalNote: boolean;
  canLogExternalContact: boolean;
  canAssignResponsible: boolean;
  canWatch: boolean;
  canUnwatch: boolean;
  canMute: boolean;
  canUnmute: boolean;
  canClose: boolean;
  canRecordShareIntent: boolean;
  canSetFollowUpOn: boolean;
  canSetPlannedFor: boolean;
  allowedStatuses: string[];
};

export type KeepRequestDetailDto = {
  requestId: string;
  referenceCode: string;
  status: string;
  currentStatusText: string | null;
  needsShare: boolean;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  description: string;
  version: string;
  pageToken: string;
  attentionLevel: string;
  attentionReason: string | null;
  priorityBand: string;
  waitingDirection: string;
  followUpOnDate: string | null;
  followUpOnReason: string | null;
  plannedForDate: string | null;
  source: string;
  intakeUrgency: string;
  contactPreference: string;
  serviceAddressLine1: string | null;
  serviceAddressLine2: string | null;
  serviceCity: string | null;
  serviceState: string | null;
  serviceZip: string | null;
  contactActions: ContactActionItem[];
  participants: ParticipantItem[];
  currentUserParticipation: CurrentUserParticipationDto;
  events: EventItem[];
  availableActions: AvailableActionsDto;
  internalNoteMaxLength: number;
};

export function useRequestDetail(id: string | undefined) {
  return useQuery({
    queryKey: ['keepRequestDetail', id],
    queryFn: () => api.get<KeepRequestDetailDto>(`/keep/requests/${id}`),
    enabled: !!id,
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}
