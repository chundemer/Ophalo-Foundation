import { StatusBar } from 'expo-status-bar';
import { Platform, StyleSheet, TouchableOpacity } from 'react-native';
import { router } from 'expo-router';

import { Text, View } from '@/components/Themed';

// Capture modal shell — S17f replaces with the full Quick Capture form.
export default function CaptureModal() {
  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Quick Capture</Text>
        <TouchableOpacity onPress={() => router.back()} accessibilityLabel="Close">
          <Text style={styles.close}>Done</Text>
        </TouchableOpacity>
      </View>
      <View style={styles.body}>
        <Text style={styles.description}>
          Capture a new request by looking up a customer phone number and recording their need.
        </Text>
      </View>
      <StatusBar style={Platform.OS === 'ios' ? 'light' : 'auto'} />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 20,
    paddingTop: 20,
    paddingBottom: 16,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: 'rgba(128,128,128,0.3)',
  },
  title: { fontSize: 17, fontWeight: '600' },
  close: { fontSize: 17, color: '#007AFF' },
  body: { flex: 1, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 32 },
  description: { fontSize: 15, textAlign: 'center', lineHeight: 22, opacity: 0.5 },
});
