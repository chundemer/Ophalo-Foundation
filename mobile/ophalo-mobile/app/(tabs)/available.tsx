import { router } from 'expo-router';
import { FlatList, RefreshControl, StyleSheet, TouchableOpacity } from 'react-native';

import { Text, View } from '@/components/Themed';
import { useColorScheme } from '@/components/useColorScheme';
import { AvailableRequestItem, useAvailableRequests } from '@/src/hooks/useAvailable';

export default function AvailableScreen() {
  const colorScheme = useColorScheme();
  const cardBg = colorScheme === 'dark' ? '#1C1C1E' : '#FFFFFF';
  const query = useAvailableRequests();
  const requests = query.data?.requests ?? [];

  return (
    <View style={styles.screen}>
      <StatusLine
        isFetching={query.isFetching}
        dataUpdatedAt={query.dataUpdatedAt}
        hasMore={query.data?.pageInfo.hasMore}
      />
      <FlatList
        data={requests}
        keyExtractor={(item) => item.requestId}
        contentContainerStyle={requests.length === 0 ? styles.emptyList : styles.list}
        refreshControl={
          <RefreshControl
            refreshing={query.isRefetching}
            onRefresh={query.refetch}
          />
        }
        renderItem={({ item }) => <AvailableRow item={item} cardBg={cardBg} />}
        ListEmptyComponent={
          <EmptyState
            isLoading={query.isLoading}
            isError={query.isError}
            onRetry={query.refetch}
          />
        }
      />
    </View>
  );
}

function AvailableRow({ item, cardBg }: { item: AvailableRequestItem; cardBg: string }) {
  const priority = normalizeLabel(item.priorityBand);
  const status = normalizeLabel(item.status);

  return (
    <TouchableOpacity
      style={[styles.row, { backgroundColor: cardBg }]}
      onPress={() => router.push({ pathname: '/requests/[id]', params: { id: item.requestId } })}
      accessibilityRole="button"
    >
      <View style={styles.rowHeader}>
        <Text style={styles.customer} numberOfLines={1}>{item.customerName}</Text>
        <Text style={styles.reference}>{item.referenceCode}</Text>
      </View>
      <Text style={styles.preview} numberOfLines={2}>{item.descriptionPreview}</Text>
      <View style={styles.metaRow}>
        <Text style={styles.statusPill}>{status}</Text>
        {priority !== 'Normal' && <Text style={styles.meta}>{priority}</Text>}
      </View>
    </TouchableOpacity>
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
  const updated = dataUpdatedAt > 0
    ? new Date(dataUpdatedAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
    : null;

  return (
    <Text style={styles.statusLine}>
      {isFetching && !updated ? 'Loading available work' : isFetching ? 'Refreshing' : updated ? `Updated ${updated}` : 'Loading available work'}
      {hasMore ? ' · More available' : ''}
    </Text>
  );
}

function EmptyState({
  isLoading,
  isError,
  onRetry,
}: {
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
}) {
  if (isLoading) {
    return (
      <View style={styles.emptyState}>
        <Text style={styles.emptyTitle}>Loading available work...</Text>
      </View>
    );
  }

  if (isError) {
    return (
      <View style={styles.emptyState}>
        <Text style={styles.emptyTitle}>Could not load available work.</Text>
        <TouchableOpacity style={styles.retryButton} onPress={onRetry}>
          <Text style={styles.retryText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={styles.emptyState}>
      <Text style={styles.emptyTitle}>No available requests.</Text>
      <Text style={styles.emptyBody}>All work is assigned. Pull to refresh.</Text>
    </View>
  );
}

function normalizeLabel(value: string): string {
  return value
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
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
  emptyList: { flexGrow: 1 },
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
