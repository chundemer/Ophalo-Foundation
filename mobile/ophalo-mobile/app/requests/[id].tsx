import { StyleSheet } from 'react-native';
import { Redirect, useLocalSearchParams } from 'expo-router';

import { Text, View } from '@/components/Themed';
import { useAuth } from '@/src/auth/AuthContext';

// Request detail — S17e fills with real fields and action affordances.
export default function RequestDetailScreen() {
  const { user } = useAuth();
  const { id } = useLocalSearchParams<{ id: string }>();

  if (!user) return <Redirect href="/signin" />;

  return (
    <View style={styles.container}>
      <Text style={styles.id}>{id}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  id: { fontSize: 13, opacity: 0.35, fontFamily: 'SpaceMono' },
});
