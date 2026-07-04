import { useState } from 'react';
import {
  ActivityIndicator,
  Linking,
  Modal,
  Pressable,
  RefreshControl,
  ScrollView,
  Share,
  StyleSheet,
  TextInput,
  TouchableOpacity,
} from 'react-native';
import { Redirect, Stack, useLocalSearchParams } from 'expo-router';

import { Text, View } from '@/components/Themed';
import { useColorScheme } from '@/components/useColorScheme';
import { useAuth } from '@/src/auth/AuthContext';
import { ApiError } from '@/src/api/client';
import {
  AvailableActionsDto,
  ContactActionItem,
  EventItem,
  useRequestDetail,
} from '@/src/hooks/useRequestDetail';
import { useLogExternalContact } from '@/src/hooks/useLogExternalContact';
import { useClearShareIntent } from '@/src/hooks/useClearShareIntent';
import { useSendBusinessUpdate } from '@/src/hooks/useSendBusinessUpdate';
import { useNetworkState } from '@/src/hooks/useNetworkState';

const PUBLIC_BASE_URL = (process.env.EXPO_PUBLIC_PUBLIC_BASE_URL ?? '').replace(/\/$/, '');

export default function RequestDetailScreen() {
  const { user } = useAuth();
  const { id } = useLocalSearchParams<{ id: string }>();
  const colorScheme = useColorScheme();
  const cardBg = colorScheme === 'dark' ? '#1C1C1E' : '#FFFFFF';
  const { data, isLoading, isError, refetch, isRefetching } = useRequestDetail(id);

  const [contactPending, setContactPending] = useState<ContactActionItem | null>(null);
  const [contactError, setContactError] = useState<string | null>(null);
  const [shareConfirmMethod, setShareConfirmMethod] = useState<
    'native_share' | 'manual_mark_shared' | null
  >(null);
  const [shareError, setShareError] = useState<string | null>(null);
  const [composerText, setComposerText] = useState('');
  const [composerError, setComposerError] = useState<string | null>(null);

  const { mutate: logContact, isPending: isLoggingContact } = useLogExternalContact();
  const { mutate: recordShareIntent, isPending: isRecordingShare } = useClearShareIntent();
  const { mutate: sendBusinessUpdate, isPending: isSendingUpdate } = useSendBusinessUpdate();
  const { isOnline } = useNetworkState();

  if (!user) return <Redirect href="/signin" />;

  if (isLoading) {
    return (
      <>
        <Stack.Screen options={{ title: 'Request' }} />
        <View style={styles.center}>
          <ActivityIndicator />
        </View>
      </>
    );
  }

  if (isError || !data) {
    return (
      <>
        <Stack.Screen options={{ title: 'Request' }} />
        <View style={styles.center}>
          <Text style={styles.errorText}>Could not load request.</Text>
          <TouchableOpacity style={styles.retryButton} onPress={() => refetch()}>
            <Text style={styles.retryText}>Retry</Text>
          </TouchableOpacity>
        </View>
      </>
    );
  }

  const status = data.currentStatusText?.trim() || normalizeLabel(data.status);
  const responsible = data.participants.find(
    (p) => p.participationType === 'responsible' && p.detachedAtUtc === null,
  );
  const availableLabels = resolveAvailableActionLabels(data.availableActions);

  const trackerUrl =
    PUBLIC_BASE_URL && data.pageToken
      ? `${PUBLIC_BASE_URL}/keep/r/${data.pageToken}`
      : null;
  const canShare =
    !!trackerUrl && data.needsShare && data.availableActions.canRecordShareIntent;

  async function handleContactTap(action: ContactActionItem) {
    const url =
      action.type === 'call'
        ? `tel:${action.target.replace(/\s/g, '')}`
        : `mailto:${action.target.trim()}`;
    try {
      const supported = await Linking.canOpenURL(url);
      if (!supported) return;
      await Linking.openURL(url);
      setContactError(null);
      setContactPending(action);
    } catch {
      // URL could not be opened — do not show log prompt
    }
  }

  function handleContactLog(outcome?: string) {
    if (!contactPending || !data) return;
    const channel = contactPending.type === 'call' ? 'phone' : 'email';
    logContact(
      { requestId: data.requestId, version: data.version, direction: 'outbound', channel, outcome },
      {
        onSuccess: () => {
          setContactPending(null);
          setContactError(null);
        },
        onError: (err) => {
          setContactError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not log contact. Please try again.',
          );
        },
      },
    );
  }

  async function handleNativeShare() {
    if (!trackerUrl) return;
    try {
      const result = await Share.share({ message: trackerUrl, url: trackerUrl });
      if (result.action === Share.sharedAction) {
        setShareError(null);
        setShareConfirmMethod('native_share');
      }
    } catch {
      // user cancelled or system error — do not prompt
    }
  }

  function handleConfirmShare() {
    if (!shareConfirmMethod || !data) return;
    recordShareIntent(
      { requestId: data.requestId, method: shareConfirmMethod },
      {
        onSuccess: () => {
          setShareConfirmMethod(null);
          setShareError(null);
        },
        onError: (err) => {
          setShareError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not record share. Please try again.',
          );
        },
      },
    );
  }

  function dismissContactSheet() {
    if (isLoggingContact) return;
    setContactPending(null);
    setContactError(null);
  }

  function dismissShareSheet() {
    if (isRecordingShare) return;
    setShareConfirmMethod(null);
    setShareError(null);
  }

  function handleSendUpdate(setStatus?: string) {
    if (!data || composerText.trim() === '') return;
    sendBusinessUpdate(
      { requestId: data.requestId, version: data.version, message: composerText.trim(), setStatus },
      {
        onSuccess: () => {
          setComposerText('');
          setComposerError(null);
        },
        onError: (err) => {
          if (
            err instanceof ApiError &&
            (err.status === 409 || err.code === 'KeepRequest.RequestChanged')
          ) {
            setComposerError('Request has changed — details refreshed. Review and try again.');
          } else if (err instanceof ApiError && err.status >= 400 && err.status < 500) {
            setComposerError('Could not send update. Please try again.');
          } else {
            setComposerError("Couldn't send. Check your connection and try again.");
          }
        },
      },
    );
  }

  return (
    <>
      <Stack.Screen options={{ title: data.referenceCode }} />
      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={refetch} />}
      >
        <Section cardBg={cardBg}>
          <Text style={styles.customerName}>{data.customerName}</Text>
          <Text style={styles.referenceCode}>{data.referenceCode}</Text>
          <View style={styles.metaRow}>
            <Text style={styles.statusPill}>{status}</Text>
            {normalizeLabel(data.priorityBand) !== 'Normal' && (
              <Text style={styles.metaTag}>{normalizeLabel(data.priorityBand)}</Text>
            )}
            {data.needsShare && <Text style={styles.metaTag}>Needs share</Text>}
          </View>
        </Section>

        <Section cardBg={cardBg}>
          <Text style={styles.sectionLabel}>Description</Text>
          <Text style={styles.bodyText}>{data.description}</Text>
        </Section>

        {(data.attentionLevel !== 'None' || data.attentionReason) && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Attention</Text>
            <FieldRow label="Level" value={normalizeLabel(data.attentionLevel)} />
            {data.attentionReason && (
              <FieldRow label="Reason" value={normalizeLabel(data.attentionReason)} />
            )}
            <FieldRow label="Waiting" value={normalizeLabel(data.waitingDirection)} />
          </Section>
        )}

        {(data.followUpOnDate || data.plannedForDate) && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Timing</Text>
            {data.followUpOnDate && (
              <FieldRow
                label="Follow up"
                value={
                  data.followUpOnReason
                    ? `${formatDateOnly(data.followUpOnDate)} · ${normalizeLabel(data.followUpOnReason)}`
                    : formatDateOnly(data.followUpOnDate)
                }
              />
            )}
            {data.plannedForDate && (
              <FieldRow label="Planned for" value={formatDateOnly(data.plannedForDate)} />
            )}
          </Section>
        )}

        <Section cardBg={cardBg}>
          <Text style={styles.sectionLabel}>Participation</Text>
          <FieldRow label="You" value={normalizeLabel(data.currentUserParticipation.participationType)} />
          {responsible && <FieldRow label="Responsible" value={responsible.displayName} />}
        </Section>

        {data.contactActions.some((c) => c.available) && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Contact</Text>
            {data.contactActions
              .filter((c) => c.available)
              .map((c, i) => (
                <TouchableOpacity
                  key={i}
                  style={styles.contactRow}
                  onPress={() => void handleContactTap(c)}
                >
                  <Text style={styles.fieldLabel}>{normalizeLabel(c.type)}</Text>
                  <Text style={styles.contactTarget}>{c.target}</Text>
                  <Text style={styles.contactChevron}>›</Text>
                </TouchableOpacity>
              ))}
          </Section>
        )}

        {canShare && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Tracker</Text>
            <TouchableOpacity
              style={[styles.actionButton, isRecordingShare && styles.actionButtonDisabled]}
              onPress={() => void handleNativeShare()}
              disabled={isRecordingShare}
            >
              <Text style={styles.actionButtonText}>Share via…</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.actionButtonOutline, isRecordingShare && styles.actionButtonDisabled]}
              onPress={() => setShareConfirmMethod('manual_mark_shared')}
              disabled={isRecordingShare}
            >
              <Text style={styles.actionButtonOutlineText}>Mark as shared</Text>
            </TouchableOpacity>
          </Section>
        )}

        {data.availableActions.canSendBusinessUpdate && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Customer Update</Text>
            <TextInput
              style={[
                styles.composerInput,
                { color: colorScheme === 'dark' ? '#FFFFFF' : '#000000' },
              ]}
              placeholder="Write a customer-visible update…"
              placeholderTextColor="rgba(128,128,128,0.6)"
              multiline
              value={composerText}
              onChangeText={setComposerText}
              editable={!isSendingUpdate}
            />
            {composerError && <Text style={styles.composerError}>{composerError}</Text>}
            {!isOnline && (
              <Text style={styles.composerOffline}>Offline — updates cannot be sent.</Text>
            )}
            <TouchableOpacity
              style={[
                styles.actionButton,
                (composerText.trim() === '' || isSendingUpdate || !isOnline) &&
                  styles.actionButtonDisabled,
              ]}
              onPress={() => handleSendUpdate()}
              disabled={composerText.trim() === '' || isSendingUpdate || !isOnline}
            >
              <Text style={styles.actionButtonText}>Send Update</Text>
            </TouchableOpacity>
            {data.availableActions.allowedStatuses.includes('resolved') && (
              <TouchableOpacity
                style={[
                  styles.actionButtonOutline,
                  (composerText.trim() === '' || isSendingUpdate || !isOnline) &&
                    styles.actionButtonDisabled,
                ]}
                onPress={() => handleSendUpdate('resolved')}
                disabled={composerText.trim() === '' || isSendingUpdate || !isOnline}
              >
                <Text style={styles.actionButtonOutlineText}>Send Update & Mark Completed</Text>
              </TouchableOpacity>
            )}
          </Section>
        )}

        {availableLabels.length > 0 && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Actions</Text>
            {availableLabels.map((label) => (
              <Text key={label} style={styles.actionLabel}>
                {label}
              </Text>
            ))}
          </Section>
        )}

        <Section cardBg={cardBg}>
          <Text style={styles.sectionLabel}>
            {`Timeline · ${data.events.length} event${data.events.length !== 1 ? 's' : ''}`}
          </Text>
          {data.events.length === 0 && (
            <Text style={styles.emptyText}>No events recorded.</Text>
          )}
          {data.events.map((event) => (
            <EventRow key={event.id} event={event} />
          ))}
        </Section>
      </ScrollView>

      {/* Phone outcome sheet */}
      <Modal
        transparent
        visible={contactPending?.type === 'call'}
        animationType="fade"
        onRequestClose={dismissContactSheet}
      >
        <Pressable style={styles.sheetOverlay} onPress={dismissContactSheet}>
          <Pressable style={[styles.sheetContainer, { backgroundColor: cardBg }]}>
            <Text style={styles.sheetTitle}>How did the call go?</Text>
            {contactError && <Text style={styles.sheetError}>{contactError}</Text>}
            {PHONE_OUTCOMES.map(({ label, value }) => (
              <TouchableOpacity
                key={value}
                style={[styles.sheetOption, isLoggingContact && styles.sheetOptionDisabled]}
                onPress={() => handleContactLog(value)}
                disabled={isLoggingContact}
              >
                <Text style={styles.sheetOptionText}>{label}</Text>
              </TouchableOpacity>
            ))}
            <TouchableOpacity
              style={styles.sheetSkip}
              onPress={dismissContactSheet}
              disabled={isLoggingContact}
            >
              <Text style={styles.sheetSkipText}>Skip</Text>
            </TouchableOpacity>
          </Pressable>
        </Pressable>
      </Modal>

      {/* Email confirm sheet */}
      <Modal
        transparent
        visible={contactPending?.type === 'email'}
        animationType="fade"
        onRequestClose={dismissContactSheet}
      >
        <Pressable style={styles.sheetOverlay} onPress={dismissContactSheet}>
          <Pressable style={[styles.sheetContainer, { backgroundColor: cardBg }]}>
            <Text style={styles.sheetTitle}>Log email sent?</Text>
            {contactPending && (
              <Text style={styles.sheetSubtitle}>{contactPending.target}</Text>
            )}
            {contactError && <Text style={styles.sheetError}>{contactError}</Text>}
            <TouchableOpacity
              style={[styles.sheetOption, isLoggingContact && styles.sheetOptionDisabled]}
              onPress={() => handleContactLog()}
              disabled={isLoggingContact}
            >
              <Text style={styles.sheetOptionText}>Log as email sent</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={styles.sheetSkip}
              onPress={dismissContactSheet}
              disabled={isLoggingContact}
            >
              <Text style={styles.sheetSkipText}>Skip</Text>
            </TouchableOpacity>
          </Pressable>
        </Pressable>
      </Modal>

      {/* Share confirm sheet */}
      <Modal
        transparent
        visible={shareConfirmMethod !== null}
        animationType="fade"
        onRequestClose={dismissShareSheet}
      >
        <Pressable style={styles.sheetOverlay} onPress={dismissShareSheet}>
          <Pressable style={[styles.sheetContainer, { backgroundColor: cardBg }]}>
            <Text style={styles.sheetTitle}>
              {shareConfirmMethod === 'native_share'
                ? 'Did you share the tracker link?'
                : 'Mark tracker as shared?'}
            </Text>
            {shareError && <Text style={styles.sheetError}>{shareError}</Text>}
            <TouchableOpacity
              style={[styles.sheetOption, isRecordingShare && styles.sheetOptionDisabled]}
              onPress={handleConfirmShare}
              disabled={isRecordingShare}
            >
              <Text style={styles.sheetOptionText}>Yes, mark as shared</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={styles.sheetSkip}
              onPress={dismissShareSheet}
              disabled={isRecordingShare}
            >
              <Text style={styles.sheetSkipText}>Cancel</Text>
            </TouchableOpacity>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}

const PHONE_OUTCOMES = [
  { label: 'Spoke with customer', value: 'spoke_with_customer' },
  { label: 'Left voicemail', value: 'left_voicemail' },
  { label: 'No answer', value: 'no_answer' },
] as const;

function Section({ children, cardBg }: { children: React.ReactNode; cardBg: string }) {
  return <View style={[styles.section, { backgroundColor: cardBg }]}>{children}</View>;
}

function FieldRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.fieldRow}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <Text style={styles.fieldValue}>{value}</Text>
    </View>
  );
}

function EventRow({ event }: { event: EventItem }) {
  const actor = event.actorDisplayName ?? normalizeLabel(event.actorType);
  const label = normalizeLabel(event.eventType);
  const ts = formatEventTime(event.occurredAtUtc);

  return (
    <View style={styles.eventRow}>
      <View style={styles.eventHeader}>
        <Text style={styles.eventActor}>{actor}</Text>
        <Text style={styles.eventMeta}>{ts}</Text>
      </View>
      <Text style={styles.eventType}>{label}</Text>
      {event.statusAfter && (
        <Text style={styles.eventMeta}>→ {normalizeLabel(event.statusAfter)}</Text>
      )}
      {event.content && <Text style={styles.eventContent}>{event.content}</Text>}
      {event.visibility === 'Internal' && (
        <Text style={styles.eventInternal}>Internal</Text>
      )}
    </View>
  );
}

const ACTION_LABELS: Partial<Record<keyof AvailableActionsDto, string>> = {
  canChangeStatus: 'Change status',
  canAddInternalNote: 'Add internal note',
  canAssignResponsible: 'Assign responsible',
  canWatch: 'Watch',
  canUnwatch: 'Unwatch',
  canMute: 'Mute notifications',
  canUnmute: 'Unmute notifications',
  canClose: 'Close',
  canSetFollowUpOn: 'Set follow-up date',
  canSetPlannedFor: 'Set planned-for date',
};

function resolveAvailableActionLabels(actions: AvailableActionsDto): string[] {
  return (Object.keys(ACTION_LABELS) as Array<keyof AvailableActionsDto>)
    .filter((key) => actions[key] === true)
    .map((key) => ACTION_LABELS[key]!);
}

function normalizeLabel(value: string): string {
  return value
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function formatDateOnly(value: string): string {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString([], {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatEventTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString([], { month: 'short', day: 'numeric' }) +
    ' ' +
    d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: { padding: 16, gap: 10, paddingBottom: 40 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32 },
  section: {
    borderRadius: 8,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: 'rgba(128,128,128,0.3)',
    padding: 14,
    gap: 6,
  },
  customerName: { fontSize: 20, fontWeight: '700' },
  referenceCode: { fontSize: 12, opacity: 0.5, fontWeight: '600' },
  metaRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginTop: 6,
    backgroundColor: 'transparent',
  },
  statusPill: {
    overflow: 'hidden',
    borderRadius: 6,
    backgroundColor: '#EAF2FF',
    color: '#174A8B',
    paddingHorizontal: 8,
    paddingVertical: 4,
    fontSize: 12,
    fontWeight: '700',
  },
  metaTag: { fontSize: 12, opacity: 0.6, fontWeight: '600' },
  sectionLabel: { fontSize: 11, fontWeight: '700', opacity: 0.5, letterSpacing: 0.5, textTransform: 'uppercase', marginBottom: 4 },
  bodyText: { fontSize: 15, lineHeight: 22, opacity: 0.85 },
  fieldRow: { flexDirection: 'row', gap: 8, backgroundColor: 'transparent' },
  fieldLabel: { fontSize: 13, opacity: 0.5, minWidth: 90 },
  fieldValue: { flex: 1, fontSize: 13, fontWeight: '600' },
  contactRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: 6,
    gap: 8,
  },
  contactTarget: { flex: 1, fontSize: 13, fontWeight: '600' },
  contactChevron: { fontSize: 18, opacity: 0.4 },
  actionButton: {
    borderRadius: 8,
    backgroundColor: '#174A8B',
    paddingVertical: 11,
    alignItems: 'center',
    marginTop: 4,
  },
  actionButtonText: { color: '#FFFFFF', fontSize: 14, fontWeight: '700' },
  actionButtonOutline: {
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#174A8B',
    paddingVertical: 10,
    alignItems: 'center',
  },
  actionButtonOutlineText: { color: '#174A8B', fontSize: 14, fontWeight: '600' },
  actionButtonDisabled: { opacity: 0.45 },
  actionLabel: { fontSize: 14, opacity: 0.8, paddingVertical: 2 },
  emptyText: { fontSize: 14, opacity: 0.5 },
  eventRow: { gap: 3, paddingTop: 10, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: 'rgba(128,128,128,0.2)' },
  eventHeader: { flexDirection: 'row', justifyContent: 'space-between', backgroundColor: 'transparent' },
  eventActor: { fontSize: 13, fontWeight: '700' },
  eventType: { fontSize: 13, opacity: 0.7 },
  eventMeta: { fontSize: 12, opacity: 0.5 },
  eventContent: { fontSize: 14, lineHeight: 20, opacity: 0.85, marginTop: 2 },
  eventInternal: { fontSize: 11, opacity: 0.45, fontStyle: 'italic' },
  composerInput: {
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: 'rgba(128,128,128,0.35)',
    borderRadius: 6,
    padding: 10,
    fontSize: 15,
    minHeight: 80,
    textAlignVertical: 'top',
    marginBottom: 6,
  },
  composerError: { fontSize: 13, color: '#C0392B', marginBottom: 6 },
  composerOffline: { fontSize: 13, opacity: 0.55, marginBottom: 6 },
  errorText: { fontSize: 16, textAlign: 'center', opacity: 0.7 },
  retryButton: { marginTop: 14, borderRadius: 8, backgroundColor: '#0057D9', paddingHorizontal: 18, paddingVertical: 10 },
  retryText: { color: '#FFFFFF', fontSize: 14, fontWeight: '700' },
  sheetOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.45)',
    justifyContent: 'flex-end',
  },
  sheetContainer: {
    borderTopLeftRadius: 16,
    borderTopRightRadius: 16,
    paddingHorizontal: 20,
    paddingTop: 20,
    paddingBottom: 36,
    gap: 0,
  },
  sheetTitle: { fontSize: 17, fontWeight: '700', marginBottom: 16 },
  sheetSubtitle: { fontSize: 13, opacity: 0.6, marginBottom: 12, marginTop: -8 },
  sheetError: { fontSize: 13, color: '#C0392B', marginBottom: 10 },
  sheetOption: {
    paddingVertical: 15,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: 'rgba(128,128,128,0.25)',
  },
  sheetOptionDisabled: { opacity: 0.45 },
  sheetOptionText: { fontSize: 16 },
  sheetSkip: { paddingVertical: 14, alignItems: 'center', marginTop: 4 },
  sheetSkipText: { fontSize: 15, opacity: 0.5 },
});
