import { StyleSheet } from 'react-native';

import { Text, View } from '@/components/Themed';
import { useBadge } from '@/src/hooks/useBadge';

export default function RequestsScreen() {
  const { data: badge } = useBadge();

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Requests</Text>
      {badge !== undefined && badge.count > 0 && (
        <Text style={styles.badge}>{badge.count} pending</Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
    fontSize: 20,
    fontWeight: 'bold',
  },
  badge: {
    marginTop: 8,
    fontSize: 15,
  },
});
