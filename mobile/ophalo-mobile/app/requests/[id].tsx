import { ActivityIndicator, RefreshControl, ScrollView, StyleSheet, TouchableOpacity } from 'react-native';
import { Redirect, Stack, useLocalSearchParams } from 'expo-router';

import { Text, View } from '@/components/Themed';
import { useColorScheme } from '@/components/useColorScheme';
import { useAuth } from '@/src/auth/AuthContext';
import {
  AvailableActionsDto,
  EventItem,
  KeepRequestDetailDto,
  useRequestDetail,
} from '@/src/hooks/useRequestDetail';

export default function RequestDetailScreen() {
  const { user } = useAuth();
  const { id } = useLocalSearchParams<{ id: string }>();
  const colorScheme = useColorScheme();
  const cardBg = colorScheme === 'dark' ? '#1C1C1E' : '#FFFFFF';
  const { data, isLoading, isError, refetch, isRefetching } = useRequestDetail(id);

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
                <FieldRow key={i} label={normalizeLabel(c.type)} value={c.target} />
              ))}
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
    </>
  );
}

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
  canSendBusinessUpdate: 'Send customer update',
  canAddInternalNote: 'Add internal note',
  canLogExternalContact: 'Log external contact',
  canAssignResponsible: 'Assign responsible',
  canWatch: 'Watch',
  canUnwatch: 'Unwatch',
  canMute: 'Mute notifications',
  canUnmute: 'Unmute notifications',
  canClose: 'Close',
  canRecordShareIntent: 'Record share intent',
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
  actionLabel: { fontSize: 14, opacity: 0.8, paddingVertical: 2 },
  emptyText: { fontSize: 14, opacity: 0.5 },
  eventRow: { gap: 3, paddingTop: 10, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: 'rgba(128,128,128,0.2)' },
  eventHeader: { flexDirection: 'row', justifyContent: 'space-between', backgroundColor: 'transparent' },
  eventActor: { fontSize: 13, fontWeight: '700' },
  eventType: { fontSize: 13, opacity: 0.7 },
  eventMeta: { fontSize: 12, opacity: 0.5 },
  eventContent: { fontSize: 14, lineHeight: 20, opacity: 0.85, marginTop: 2 },
  eventInternal: { fontSize: 11, opacity: 0.45, fontStyle: 'italic' },
  errorText: { fontSize: 16, textAlign: 'center', opacity: 0.7 },
  retryButton: { marginTop: 14, borderRadius: 8, backgroundColor: '#0057D9', paddingHorizontal: 18, paddingVertical: 10 },
  retryText: { color: '#FFFFFF', fontSize: 14, fontWeight: '700' },
});
