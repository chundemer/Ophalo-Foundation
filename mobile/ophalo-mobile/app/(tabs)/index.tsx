import { router } from 'expo-router';
import { useState } from 'react';
import { FlatList, RefreshControl, StyleSheet, TouchableOpacity } from 'react-native';

import { Text, View } from '@/components/Themed';
import { useColorScheme } from '@/components/useColorScheme';
import { useBadge } from '@/src/hooks/useBadge';
import { KeepRequestSummary, MyWorkView, useMyWork } from '@/src/hooks/useMyWork';

type Segment = {
  label: string;
  view: MyWorkView;
};

const segments: Segment[] = [
  { label: 'My Promises', view: 'assigned_to_me' },
  { label: 'Watching', view: 'watching' },
];

export default function MyWorkScreen() {
  const [activeView, setActiveView] = useState<MyWorkView>('assigned_to_me');
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const colorScheme = useColorScheme();
  const segBg = colorScheme === 'dark' ? '#2C2C2E' : '#ECEFF3';
  const segSelectedBg = colorScheme === 'dark' ? '#3A3A3C' : '#FFFFFF';
  const { data: badge } = useBadge();
  const promises = useMyWork('assigned_to_me');
  const watching = useMyWork('watching');
  const activeQuery = activeView === 'assigned_to_me' ? promises : watching;
  const requests = activeQuery.data?.requests ?? [];

  function handleRefresh() {
    setIsManualRefreshing(true);
    void activeQuery.refetch().finally(() => setIsManualRefreshing(false));
  }

  return (
    <View style={styles.screen}>
      <View style={styles.header}>
        <View>
          <Text style={styles.title}>My Work</Text>
          <Text style={styles.subtitle}>
            {badge !== undefined && badge.count > 0 ? `${badge.count} pending` : 'Active field work'}
          </Text>
        </View>
      </View>

      <View style={[styles.segmentedControl, { backgroundColor: segBg }]}>
        {segments.map((segment) => {
          const selected = activeView === segment.view;
          const count = segment.view === 'assigned_to_me'
            ? promises.data?.requests.length
            : watching.data?.requests.length;
          return (
            <TouchableOpacity
              key={segment.view}
              style={[styles.segment, selected && { backgroundColor: segSelectedBg }]}
              onPress={() => setActiveView(segment.view)}
              accessibilityRole="button"
              accessibilityState={{ selected }}
            >
              <Text style={[styles.segmentText, selected && styles.segmentTextSelected]}>
                {count === undefined ? segment.label : `${segment.label} (${count})`}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>

      <StatusLine
        isFetching={activeQuery.isFetching}
        dataUpdatedAt={activeQuery.dataUpdatedAt}
        hasMore={activeQuery.data?.pageInfo.hasMore}
      />

      <FlatList
        data={requests}
        keyExtractor={(item) => item.id}
        contentContainerStyle={requests.length === 0 ? styles.emptyList : styles.list}
        refreshControl={(
          <RefreshControl
            refreshing={isManualRefreshing}
            onRefresh={handleRefresh}
          />
        )}
        renderItem={({ item }) => <MyWorkRow request={item} />}
        ListEmptyComponent={(
          <EmptyState
            isLoading={activeQuery.isLoading}
            isError={activeQuery.isError}
            onRetry={activeQuery.refetch}
            view={activeView}
          />
        )}
      />
    </View>
  );
}

function MyWorkRow({ request }: { request: KeepRequestSummary }) {
  const colorScheme = useColorScheme();
  const cardBg = colorScheme === 'dark' ? '#1C1C1E' : '#FFFFFF';
  const preview = request.preview.previewText?.trim() || request.description;
  const priority = normalizeLabel(request.attention.priorityBand);
  const status = request.currentStatusText?.trim() || normalizeLabel(request.status);
  const timing = request.timing.followUpOnLabel ?? request.timing.plannedForLabel;

  return (
    <TouchableOpacity
      style={[styles.row, { backgroundColor: cardBg }]}
      onPress={() => router.push({ pathname: '/requests/[id]', params: { id: request.id } })}
      accessibilityRole="button"
    >
      <View style={styles.rowHeader}>
        <Text style={styles.customer} numberOfLines={1}>{request.customerName}</Text>
        <Text style={styles.reference}>{request.referenceCode}</Text>
      </View>
      {request.participation.responsibleDisplayName && (
        <Text style={styles.responsible} numberOfLines={1}>{request.participation.responsibleDisplayName}</Text>
      )}
      <Text style={styles.preview} numberOfLines={2}>{preview}</Text>
      <View style={styles.metaRow}>
        <Text style={styles.statusPill}>{status}</Text>
        {priority !== 'Normal' && <Text style={styles.meta}>{priority}</Text>}
        {request.needsShare && <Text style={styles.meta}>Needs share</Text>}
        {timing && <Text style={styles.meta}>{timing}</Text>}
      </View>
    </TouchableOpacity>
  );
}

function EmptyState({
  isLoading,
  isError,
  onRetry,
  view,
}: {
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  view: MyWorkView;
}) {
  if (isLoading) {
    return (
      <View style={styles.emptyState}>
        <Text style={styles.emptyTitle}>Loading work...</Text>
      </View>
    );
  }

  if (isError) {
    return (
      <View style={styles.emptyState}>
        <Text style={styles.emptyTitle}>Could not load work.</Text>
        <TouchableOpacity style={styles.retryButton} onPress={onRetry}>
          <Text style={styles.retryText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={styles.emptyState}>
      <Text style={styles.emptyTitle}>
        {view === 'assigned_to_me' ? 'No promises assigned to you.' : 'No watched requests.'}
      </Text>
      <Text style={styles.emptyBody}>Pull to refresh for the latest work.</Text>
    </View>
  );
}

function StatusLine({
  isFetching,
  dataUpdatedAt,
  hasMore,
}: {
  isFetching: boolean;
  dataUpdatedAt: number;
  hasMore?: boolean;
}) {
  const updated = dataUpdatedAt > 0 ? new Date(dataUpdatedAt).toLocaleTimeString([], {
    hour: 'numeric',
    minute: '2-digit',
  }) : null;

  return (
    <Text style={styles.statusLine}>
      {isFetching && updated ? 'Refreshing cached list' : updated ? `Updated ${updated}` : 'Loading latest list'}
      {hasMore ? ' · More available later' : ''}
    </Text>
  );
}

function normalizeLabel(value: string): string {
  return value
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    paddingTop: 18,
  },
  header: {
    paddingHorizontal: 20,
    paddingBottom: 14,
  },
  title: {
    fontSize: 28,
    fontWeight: '700',
  },
  subtitle: {
    marginTop: 4,
    fontSize: 14,
    opacity: 0.6,
  },
  segmentedControl: {
    flexDirection: 'row',
    marginHorizontal: 20,
    padding: 3,
    borderRadius: 8,
  },
  segment: {
    flex: 1,
    minHeight: 36,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 6,
    paddingHorizontal: 8,
  },
  segmentSelected: {},
  segmentText: {
    fontSize: 13,
    fontWeight: '600',
    opacity: 0.6,
  },
  segmentTextSelected: {
    opacity: 1,
  },
  statusLine: {
    paddingHorizontal: 20,
    paddingTop: 10,
    paddingBottom: 8,
    fontSize: 12,
    opacity: 0.55,
  },
  list: {
    paddingHorizontal: 16,
    paddingBottom: 24,
    gap: 10,
  },
  emptyList: {
    flexGrow: 1,
  },
  row: {
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: 'rgba(128,128,128,0.3)',
    borderRadius: 8,
    padding: 14,
  },
  rowHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 12,
    backgroundColor: 'transparent',
  },
  customer: {
    flex: 1,
    fontSize: 16,
    fontWeight: '700',
  },
  reference: {
    fontSize: 12,
    opacity: 0.5,
    fontWeight: '600',
  },
  responsible: {
    fontSize: 12,
    opacity: 0.55,
    marginTop: 3,
    fontWeight: '500',
  },
  preview: {
    marginTop: 8,
    fontSize: 14,
    lineHeight: 20,
    opacity: 0.75,
  },
  metaRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: 8,
    marginTop: 12,
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
  meta: {
    fontSize: 12,
    opacity: 0.6,
    fontWeight: '600',
  },
  emptyState: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
  },
  emptyTitle: {
    textAlign: 'center',
    fontSize: 17,
    fontWeight: '700',
  },
  emptyBody: {
    marginTop: 8,
    textAlign: 'center',
    fontSize: 14,
    opacity: 0.55,
  },
  retryButton: {
    marginTop: 14,
    borderRadius: 8,
    backgroundColor: '#0057D9',
    paddingHorizontal: 18,
    paddingVertical: 10,
  },
  retryText: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '700',
  },
});
