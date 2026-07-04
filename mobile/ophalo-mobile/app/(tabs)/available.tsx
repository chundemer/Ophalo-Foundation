import { StyleSheet } from 'react-native';

import { Text, View } from '@/components/Themed';

// Available work list — S17d wires the real query and intentional empty state.
export default function AvailableScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.empty}>No requests available.</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  empty: { fontSize: 15, opacity: 0.4 },
});
