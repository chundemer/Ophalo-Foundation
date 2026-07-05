import { useState } from 'react';
import {
  ActivityIndicator,
  Linking,
  Modal,
  Platform,
  Pressable,
  RefreshControl,
  ScrollView,
  Share,
  StyleSheet,
  TextInput,
  TouchableOpacity,
} from 'react-native';
import DateTimePicker from '@react-native-community/datetimepicker';
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
import { useWatchRequest, useUnwatchRequest } from '@/src/hooks/useWatchRequest';
import { useMuteRequest, useUnmuteRequest } from '@/src/hooks/useMuteRequest';
import { useAssignResponsible } from '@/src/hooks/useAssignResponsible';
import { useSetFollowUpOn, useClearFollowUpOn } from '@/src/hooks/useFollowUpOn';
import { useSetPlannedFor, useClearPlannedFor } from '@/src/hooks/usePlannedFor';
import { useNetworkState } from '@/src/hooks/useNetworkState';

const PUBLIC_BASE_URL = (process.env.EXPO_PUBLIC_PUBLIC_BASE_URL ?? '').replace(/\/$/, '');

const FOLLOW_UP_REASONS: { label: string; value: string }[] = [
  { label: 'Waiting on customer', value: 'customer_delay' },
  { label: 'Waiting on parts',    value: 'parts' },
  { label: 'Weather',             value: 'weather' },
  { label: 'Need to schedule',    value: 'business_operator_availability' },
  { label: 'Third party',         value: 'third_party' },
  { label: 'Other',               value: 'other' },
];

export default function RequestDetailScreen() {
  const { user } = useAuth();
  const { id } = useLocalSearchParams<{ id: string }>();

  if (!user) return <Redirect href="/signin" />;

  return <RequestDetailContent id={id} userAccountUserId={user.accountUserId} />;
}

function RequestDetailContent({
  id,
  userAccountUserId,
}: {
  id: string;
  userAccountUserId: string;
}) {
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
  const [assignError, setAssignError] = useState<string | null>(null);
  const [watchError, setWatchError] = useState<string | null>(null);
  const [muteError, setMuteError] = useState<string | null>(null);
  const [followUpError, setFollowUpError] = useState<string | null>(null);
  const [plannedForError, setPlannedForError] = useState<string | null>(null);

  const { mutate: logContact, isPending: isLoggingContact } = useLogExternalContact();
  const { mutate: recordShareIntent, isPending: isRecordingShare } = useClearShareIntent();
  const { mutate: sendBusinessUpdate, isPending: isSendingUpdate } = useSendBusinessUpdate();
  const { mutate: watchRequest, isPending: isWatching } = useWatchRequest();
  const { mutate: unwatchRequest, isPending: isUnwatching } = useUnwatchRequest();
  const { mutate: muteRequest, isPending: isMuting } = useMuteRequest();
  const { mutate: unmuteRequest, isPending: isUnmuting } = useUnmuteRequest();
  const { mutate: assignResponsible, isPending: isAssigning } = useAssignResponsible();
  const { mutate: setFollowUpOn, isPending: isSettingFollowUp } = useSetFollowUpOn();
  const { mutate: clearFollowUpOn, isPending: isClearingFollowUp } = useClearFollowUpOn();
  const { mutate: setPlannedFor, isPending: isSettingPlannedFor } = useSetPlannedFor();
  const { mutate: clearPlannedFor, isPending: isClearingPlannedFor } = useClearPlannedFor();
  const { isOnline } = useNetworkState();

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
  const canRecordShare = data.needsShare && data.availableActions.canRecordShareIntent;
  const canShare = !!trackerUrl && canRecordShare;

  async function handleContactTap(action: ContactActionItem) {
    const url =
      action.type === 'call'
        ? `tel:${action.target.replace(/\s/g, '')}`
        : `mailto:${action.target.trim()}`;
    try {
      const supported = await Linking.canOpenURL(url);
      if (!supported) {
        setContactError(
          action.type === 'call'
            ? "Calling isn't available on this device."
            : "Email isn't available on this device.",
        );
        return;
      }
      await Linking.openURL(url);
      setContactError(null);
      setContactPending(action);
    } catch {
      // URL could not be opened — do not show log prompt
      setContactError('Could not open this contact action on this device.');
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

  function handleWatch() {
    if (!data) return;
    watchRequest(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setWatchError(null),
        onError: (err) => {
          setWatchError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not complete. Please try again.',
          );
        },
      },
    );
  }

  function handleUnwatch() {
    if (!data) return;
    unwatchRequest(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setWatchError(null),
        onError: (err) => {
          setWatchError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not complete. Please try again.',
          );
        },
      },
    );
  }

  function handleMute() {
    if (!data) return;
    muteRequest(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setMuteError(null),
        onError: (err) => {
          setMuteError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not complete. Please try again.',
          );
        },
      },
    );
  }

  function handleUnmute() {
    if (!data) return;
    unmuteRequest(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setMuteError(null),
        onError: (err) => {
          setMuteError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not complete. Please try again.',
          );
        },
      },
    );
  }

  function handleAssignSelf() {
    if (!data) return;
    assignResponsible(
      { requestId: data.requestId, version: data.version, accountUserId: userAccountUserId },
      {
        onSuccess: () => setAssignError(null),
        onError: (err) => {
          if (
            err instanceof ApiError &&
            err.code === 'KeepRequest.ParticipationRequestAlreadyAssigned'
          ) {
            setAssignError('Already claimed — details refreshed.');
          } else if (err instanceof ApiError && err.status === 409) {
            setAssignError('Request has changed — details refreshed.');
          } else {
            setAssignError('Could not assign. Please try again.');
          }
        },
      },
    );
  }

  function handleSetFollowUp(dateStr: string, reason?: string) {
    if (!data) return;
    setFollowUpOn(
      { requestId: data.requestId, version: data.version, date: dateStr, reason },
      {
        onSuccess: () => setFollowUpError(null),
        onError: (err) => {
          if (err instanceof ApiError) {
            if (err.code === 'KeepRequest.RequestChanged') {
              setFollowUpError('Request has changed — refresh and try again.');
            } else if (err.status === 409) {
              setFollowUpError('This request is no longer active — a follow-up date cannot be set.');
            } else {
              setFollowUpError('Follow-up date could not be saved. Check your connection and try again.');
            }
          } else {
            setFollowUpError('Follow-up date could not be saved. Check your connection and try again.');
          }
        },
      },
    );
  }

  function handleClearFollowUp() {
    if (!data) return;
    clearFollowUpOn(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setFollowUpError(null),
        onError: (err) => {
          if (err instanceof ApiError && err.code === 'KeepRequest.RequestChanged') {
            setFollowUpError('Request has changed — refresh and try again.');
          } else if (err instanceof ApiError && err.status === 409) {
            setFollowUpError('This request is no longer active — the follow-up date cannot be cleared.');
          } else {
            setFollowUpError('Could not clear follow-up date. Check your connection and try again.');
          }
        },
      },
    );
  }

  function handleSetPlannedFor(dateStr: string) {
    if (!data) return;
    setPlannedFor(
      { requestId: data.requestId, version: data.version, date: dateStr },
      {
        onSuccess: () => setPlannedForError(null),
        onError: (err) => {
          setPlannedForError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not set planned-for date. Please try again.',
          );
        },
      },
    );
  }

  function handleClearPlannedFor() {
    if (!data) return;
    clearPlannedFor(
      { requestId: data.requestId, version: data.version },
      {
        onSuccess: () => setPlannedForError(null),
        onError: (err) => {
          setPlannedForError(
            err instanceof ApiError && err.status === 409
              ? 'Request has changed — details refreshed.'
              : 'Could not clear planned-for date. Please try again.',
          );
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

        {(data.attentionLevel.toLowerCase() !== 'none' || data.attentionReason) && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Attention</Text>
            <FieldRow label="Level" value={normalizeLabel(data.attentionLevel)} />
            {data.attentionReason && (
              <FieldRow label="Reason" value={normalizeLabel(data.attentionReason)} />
            )}
            <FieldRow label="Waiting" value={normalizeLabel(data.waitingDirection)} />
          </Section>
        )}

        {(data.followUpOnDate ||
          data.plannedForDate ||
          data.availableActions.canSetFollowUpOn ||
          data.availableActions.canSetPlannedFor) && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Timing</Text>
            {(data.followUpOnDate || data.availableActions.canSetFollowUpOn) && (
              <DateSheetPicker
                label="Follow up on"
                subtitle="Remind me to check back or re-engage with this customer."
                existingDate={data.followUpOnDate}
                existingReason={data.followUpOnReason}
                reasons={FOLLOW_UP_REASONS}
                onSave={handleSetFollowUp}
                onClear={handleClearFollowUp}
                isPending={isSettingFollowUp || isClearingFollowUp}
                isOnline={isOnline}
                error={followUpError}
                cardBg={cardBg}
                readOnly={!data.availableActions.canSetFollowUpOn}
              />
            )}
            {(data.followUpOnDate || data.availableActions.canSetFollowUpOn) &&
              (data.plannedForDate || data.availableActions.canSetPlannedFor) && (
              <View style={styles.timingSeparator} />
            )}
            {(data.plannedForDate || data.availableActions.canSetPlannedFor) && (
              <DateSheetPicker
                label="Planned for"
                subtitle="The work is expected or scheduled for this date."
                existingDate={data.plannedForDate}
                onSave={handleSetPlannedFor}
                onClear={handleClearPlannedFor}
                isPending={isSettingPlannedFor || isClearingPlannedFor}
                isOnline={isOnline}
                error={plannedForError}
                cardBg={cardBg}
                readOnly={!data.availableActions.canSetPlannedFor}
              />
            )}
          </Section>
        )}

        <Section cardBg={cardBg}>
          <Text style={styles.sectionLabel}>Participation</Text>
          <FieldRow label="You" value={normalizeLabel(data.currentUserParticipation.participationType)} />
          {responsible && <FieldRow label="Responsible" value={responsible.displayName} />}
          {data.availableActions.canAssignResponsible &&
            data.currentUserParticipation.participationType.toLowerCase() !== 'responsible' && (
            <TouchableOpacity
              style={[
                styles.actionButton,
                (isAssigning || !isOnline) && styles.actionButtonDisabled,
              ]}
              onPress={handleAssignSelf}
              disabled={isAssigning || !isOnline}
            >
              <Text style={styles.actionButtonText}>Assign to me</Text>
            </TouchableOpacity>
          )}
          {assignError && <Text style={styles.actionError}>{assignError}</Text>}
          {data.availableActions.canWatch && (
            <TouchableOpacity
              style={[
                styles.actionButtonOutline,
                (isWatching || !isOnline) && styles.actionButtonDisabled,
              ]}
              onPress={handleWatch}
              disabled={isWatching || !isOnline}
            >
              <Text style={styles.actionButtonOutlineText}>Watch</Text>
            </TouchableOpacity>
          )}
          {data.availableActions.canUnwatch && (
            <TouchableOpacity
              style={[
                styles.actionButtonOutline,
                (isUnwatching || !isOnline) && styles.actionButtonDisabled,
              ]}
              onPress={handleUnwatch}
              disabled={isUnwatching || !isOnline}
            >
              <Text style={styles.actionButtonOutlineText}>Unwatch</Text>
            </TouchableOpacity>
          )}
          {watchError && <Text style={styles.actionError}>{watchError}</Text>}
          {data.availableActions.canMute && (
            <TouchableOpacity
              style={[
                styles.actionButtonOutline,
                (isMuting || !isOnline) && styles.actionButtonDisabled,
              ]}
              onPress={handleMute}
              disabled={isMuting || !isOnline}
            >
              <Text style={styles.actionButtonOutlineText}>Mute notifications</Text>
            </TouchableOpacity>
          )}
          {data.availableActions.canUnmute && (
            <TouchableOpacity
              style={[
                styles.actionButtonOutline,
                (isUnmuting || !isOnline) && styles.actionButtonDisabled,
              ]}
              onPress={handleUnmute}
              disabled={isUnmuting || !isOnline}
            >
              <Text style={styles.actionButtonOutlineText}>Unmute notifications</Text>
            </TouchableOpacity>
          )}
          {muteError && <Text style={styles.actionError}>{muteError}</Text>}
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

        {canRecordShare && (
          <Section cardBg={cardBg}>
            <Text style={styles.sectionLabel}>Tracker</Text>
            {canShare && (
              <TouchableOpacity
                style={[styles.actionButton, isRecordingShare && styles.actionButtonDisabled]}
                onPress={() => void handleNativeShare()}
                disabled={isRecordingShare}
              >
                <Text style={styles.actionButtonText}>Share via…</Text>
              </TouchableOpacity>
            )}
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
                style={[styles.sheetOption, (isLoggingContact || !isOnline) && styles.sheetOptionDisabled]}
                onPress={() => handleContactLog(value)}
                disabled={isLoggingContact || !isOnline}
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
              style={[styles.sheetOption, (isLoggingContact || !isOnline) && styles.sheetOptionDisabled]}
              onPress={() => handleContactLog()}
              disabled={isLoggingContact || !isOnline}
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
              style={[styles.sheetOption, (isRecordingShare || !isOnline) && styles.sheetOptionDisabled]}
              onPress={handleConfirmShare}
              disabled={isRecordingShare || !isOnline}
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

function DateSheetPicker({
  label,
  subtitle,
  existingDate,
  existingReason,
  reasons,
  onSave,
  onClear,
  isPending,
  isOnline,
  error,
  cardBg,
  readOnly = false,
}: {
  label: string;
  subtitle?: string;
  existingDate: string | null;
  existingReason?: string | null;
  reasons?: { label: string; value: string }[];
  onSave: (dateStr: string, reason?: string) => void;
  onClear: () => void;
  isPending: boolean;
  isOnline: boolean;
  error: string | null;
  cardBg: string;
  readOnly?: boolean;
}) {
  const [sheetVisible, setSheetVisible] = useState(false);
  const [pickerDate, setPickerDate] = useState(new Date());
  const [selectedReason, setSelectedReason] = useState<string | null>(null);
  const disabled = isPending || !isOnline;

  function openSheet() {
    const today = startOfToday();
    const initial = existingDate ? parseLocalDate(existingDate) : today;
    setPickerDate(initial >= today ? initial : today);
    setSelectedReason(existingReason ?? null);
    setSheetVisible(true);
  }

  function handleSave() {
    setSheetVisible(false);
    onSave(toDateStr(pickerDate), selectedReason ?? undefined);
  }

  function handleClear() {
    setSheetVisible(false);
    onClear();
  }

  function handleChip(daysFromNow: number) {
    const d = new Date();
    d.setDate(d.getDate() + daysFromNow);
    setSheetVisible(false);
    onSave(toDateStr(d), selectedReason ?? undefined);
  }

  const todayDow = new Date().getDay();
  const daysToFriday = (5 - todayDow + 7) % 7;
  const chips: [string, number][] = [
    ['Today', 0],
    ['Tomorrow', 1],
    ...(daysToFriday > 1 ? [['This Friday', daysToFriday] as [string, number]] : []),
    ['Next week', 7],
  ];

  if (readOnly) {
    if (!existingDate) return null;
    return (
      <View style={styles.dateDisplayRow}>
        <Text style={styles.dateDisplayLabel}>{label}</Text>
        <Text style={styles.dateDisplayValue}>{formatFriendlyDate(existingDate)}</Text>
      </View>
    );
  }

  return (
    <>
      {existingDate ? (
        <TouchableOpacity style={styles.dateSetRow} onPress={openSheet} disabled={disabled}>
          <View style={styles.dateSetInfo}>
            <Text style={styles.dateSetLabel}>{label}</Text>
            <Text style={styles.dateSetValue}>{formatFriendlyDate(existingDate)}</Text>
          </View>
          <Text style={[styles.dateSetChange, disabled && { opacity: 0.4 }]}>Change</Text>
        </TouchableOpacity>
      ) : (
        <TouchableOpacity
          style={[styles.dateTrigger, disabled && styles.actionButtonDisabled]}
          onPress={openSheet}
          disabled={disabled}
        >
          <Text style={styles.dateTriggerText}>{label}</Text>
        </TouchableOpacity>
      )}
      {error && <Text style={styles.actionError}>{error}</Text>}

      <Modal
        transparent
        visible={sheetVisible}
        animationType="slide"
        onRequestClose={() => setSheetVisible(false)}
      >
        <Pressable style={styles.pickerOverlay} onPress={() => setSheetVisible(false)}>
          <Pressable style={[styles.pickerSheet, { backgroundColor: cardBg }]}>
            <View style={styles.pickerHeader}>
              <TouchableOpacity onPress={() => setSheetVisible(false)}>
                <Text style={styles.pickerCancel}>Cancel</Text>
              </TouchableOpacity>
              <View style={styles.pickerHeaderCenter}>
                <Text style={styles.pickerTitle}>{label}</Text>
                {subtitle && <Text style={styles.pickerSubtitle}>{subtitle}</Text>}
              </View>
              <TouchableOpacity onPress={handleSave} disabled={disabled}>
                <Text style={[styles.pickerSave, disabled && { opacity: 0.4 }]}>Save</Text>
              </TouchableOpacity>
            </View>

            <View style={styles.pickerChipRow}>
              {chips.map(([chipLabel, days]) => (
                <TouchableOpacity
                  key={chipLabel}
                  style={[styles.pickerChip, disabled && styles.pickerChipDisabled]}
                  onPress={() => handleChip(days)}
                  disabled={disabled}
                >
                  <Text style={styles.pickerChipText}>{chipLabel}</Text>
                </TouchableOpacity>
              ))}
            </View>

            {reasons && reasons.length > 0 && (
              <View style={styles.reasonSection}>
                <Text style={styles.reasonSectionLabel}>Reason (optional)</Text>
                <View style={styles.reasonChipRow}>
                  {reasons.map((r) => {
                    const isSelected = selectedReason === r.value;
                    return (
                      <TouchableOpacity
                        key={r.value}
                        style={[
                          styles.reasonChip,
                          isSelected && styles.reasonChipSelected,
                          disabled && styles.pickerChipDisabled,
                        ]}
                        onPress={() => setSelectedReason(isSelected ? null : r.value)}
                        disabled={disabled}
                        accessibilityRole="checkbox"
                        accessibilityState={{ checked: isSelected }}
                      >
                        <Text style={[styles.reasonChipText, isSelected && styles.reasonChipTextSelected]}>
                          {r.label}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>
              </View>
            )}

            <DateTimePicker
              mode="date"
              display="spinner"
              value={pickerDate}
              minimumDate={startOfToday()}
              onValueChange={(_event, date) => setPickerDate(date)}
            />

            {existingDate && (
              <TouchableOpacity
                style={[styles.pickerClearButton, disabled && styles.actionButtonDisabled]}
                onPress={handleClear}
                disabled={disabled}
              >
                <Text style={styles.pickerClearText}>Clear date</Text>
              </TouchableOpacity>
            )}
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}

function FieldRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.fieldRow}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <Text style={styles.fieldValue}>{value}</Text>
    </View>
  );
}

const FOLLOW_UP_REASON_LABELS: Record<string, string> = {
  weather: 'Weather',
  parts: 'Waiting on parts',
  customer_delay: 'Waiting on customer',
  business_operator_availability: 'Need to schedule',
  third_party: 'Third party',
  other: 'Other',
};

function timingEventDetail(event: EventItem): string | null {
  if (event.eventType === 'planned_for_changed') {
    return event.plannedForDate
      ? `Planned date set to ${formatDateOnly(event.plannedForDate)}`
      : 'Planned date removed';
  }
  if (event.eventType === 'follow_up_on_changed') {
    if (!event.followUpOnDate) return 'Follow-up removed';
    const base = `Follow-up set for ${formatDateOnly(event.followUpOnDate)}`;
    const reasonLabel = event.followUpOnReason ? FOLLOW_UP_REASON_LABELS[event.followUpOnReason] : null;
    return reasonLabel ? `${base} · ${reasonLabel}` : base;
  }
  return null;
}

function EventRow({ event }: { event: EventItem }) {
  const actor = event.actorDisplayName ?? normalizeLabel(event.actorType);
  const label = normalizeLabel(event.eventType);
  const ts = formatEventTime(event.occurredAtUtc);
  const timingDetail = timingEventDetail(event);

  return (
    <View style={styles.eventRow}>
      <View style={styles.eventHeader}>
        <Text style={styles.eventActor}>{actor}</Text>
        <Text style={styles.eventMeta}>{ts}</Text>
      </View>
      <Text style={styles.eventType}>{label}</Text>
      {timingDetail && (
        <Text style={styles.eventMeta}>{timingDetail}</Text>
      )}
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
  canClose: 'Close',
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

function startOfToday(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

function parseLocalDate(dateStr: string): Date {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, d);
}

function toDateStr(d: Date): string {
  const y = d.getFullYear();
  const mo = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${mo}-${day}`;
}

function formatFriendlyDate(dateStr: string): string {
  return parseLocalDate(dateStr).toLocaleDateString([], {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  });
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
  actionError: { fontSize: 13, color: '#C0392B', marginTop: 2 },
  timingSeparator: {
    height: StyleSheet.hairlineWidth,
    backgroundColor: 'rgba(128,128,128,0.25)',
    marginVertical: 6,
  },
  dateDisplayRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: 'transparent',
    gap: 8,
  },
  dateDisplayLabel: { fontSize: 13, opacity: 0.5, minWidth: 90 },
  dateDisplayValue: { flex: 1, fontSize: 13, fontWeight: '600' },
  dateTrigger: {
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#174A8B',
    paddingVertical: 10,
    alignItems: 'center',
  },
  dateTriggerText: { color: '#174A8B', fontSize: 14, fontWeight: '600' },
  dateSetRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: 'transparent',
    paddingVertical: 2,
  },
  dateSetInfo: { gap: 1 },
  dateSetLabel: {
    fontSize: 11,
    opacity: 0.5,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.4,
  },
  dateSetValue: { fontSize: 15, fontWeight: '600' },
  dateSetChange: { fontSize: 14, color: '#174A8B', fontWeight: '500' },
  pickerOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.45)',
    justifyContent: 'flex-end',
  },
  pickerSheet: {
    borderTopLeftRadius: 16,
    borderTopRightRadius: 16,
    paddingBottom: 36,
  },
  pickerHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 4,
    backgroundColor: 'transparent',
  },
  pickerHeaderCenter: { alignItems: 'center', flex: 1 },
  pickerTitle: { fontSize: 16, fontWeight: '700', textAlign: 'center' },
  pickerSubtitle: { fontSize: 12, opacity: 0.55, textAlign: 'center', marginTop: 2 },
  pickerCancel: { fontSize: 16, color: '#174A8B', minWidth: 60 },
  pickerSave: { fontSize: 16, fontWeight: '700', color: '#174A8B', minWidth: 60, textAlign: 'right' },
  pickerChipRow: {
    flexDirection: 'row',
    paddingHorizontal: 16,
    gap: 8,
    flexWrap: 'wrap',
    marginTop: 4,
    marginBottom: 4,
    backgroundColor: 'transparent',
  },
  pickerChip: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: '#174A8B',
    paddingHorizontal: 12,
    paddingVertical: 6,
  },
  pickerChipDisabled: { opacity: 0.4 },
  pickerChipText: { fontSize: 13, color: '#174A8B', fontWeight: '600' },
  reasonSection: {
    paddingHorizontal: 16,
    paddingTop: 4,
    backgroundColor: 'transparent',
  },
  reasonSectionLabel: {
    fontSize: 11,
    fontWeight: '700',
    opacity: 0.5,
    letterSpacing: 0.5,
    textTransform: 'uppercase',
    marginBottom: 8,
  },
  reasonChipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    backgroundColor: 'transparent',
  },
  reasonChip: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: '#168A9A',
    paddingHorizontal: 12,
    paddingVertical: 6,
  },
  reasonChipSelected: {
    backgroundColor: '#168A9A',
    borderColor: '#168A9A',
  },
  reasonChipText: { fontSize: 13, color: '#168A9A', fontWeight: '500' },
  reasonChipTextSelected: { color: '#FFFFFF', fontWeight: '600' },
  pickerClearButton: {
    marginHorizontal: 20,
    marginTop: 4,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: '#C0392B',
    paddingVertical: 10,
    alignItems: 'center',
  },
  pickerClearText: { color: '#C0392B', fontSize: 14, fontWeight: '600' },
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
