import Constants from 'expo-constants';
import * as WebBrowser from 'expo-web-browser';
import { Alert, ScrollView, StyleSheet, TouchableOpacity } from 'react-native';

import { Text, View } from '@/components/Themed';
import { useAuth } from '@/src/auth/AuthContext';

const WEB_URL = (process.env.EXPO_PUBLIC_WEB_URL ?? 'https://ophalo.com').replace(/\/$/, '');

function validOpHaloHttpsUrl(raw: string): string | null {
  if (!raw) return null;
  try {
    const u = new URL(raw);
    if (u.protocol !== 'https:') return null;
    if (u.hostname !== 'ophalo.com' && !u.hostname.endsWith('.ophalo.com')) return null;
    return u.href;
  } catch {
    return null;
  }
}

function validHttpsUrl(raw: string): string | null {
  if (!raw) return null;
  try {
    const u = new URL(raw);
    return u.protocol === 'https:' ? u.href : null;
  } catch {
    return null;
  }
}

const DELETION_URL = validOpHaloHttpsUrl(process.env.EXPO_PUBLIC_ACCOUNT_DELETION_URL ?? '');
const SUPPORT_URL = validHttpsUrl(process.env.EXPO_PUBLIC_SUPPORT_URL ?? '');
const PRIVACY_URL = `${WEB_URL}/privacy`;
const APP_VERSION = Constants.expoConfig?.version ?? '—';

function capitalize(s: string): string {
  return s ? s.charAt(0).toUpperCase() + s.slice(1) : '—';
}

export default function AccountScreen() {
  const { user, logout } = useAuth();

  function handleLogout() {
    Alert.alert('Sign Out', 'Sign out of OpHalo Keep?', [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Sign Out', style: 'destructive', onPress: () => { void logout(); } },
    ]);
  }

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <View style={styles.section}>
        <Text style={styles.sectionHeader}>ACCOUNT</Text>
        <View style={styles.card}>
          <Row label="Role" value={capitalize(user?.accountRole ?? '')} />
          <Divider />
          <Row label="User" value={(user?.accountUserId ?? '').slice(0, 8)} />
        </View>
      </View>

      <View style={styles.section}>
        <Text style={styles.sectionHeader}>INFORMATION</Text>
        <View style={styles.card}>
          <LinkRow label="Privacy Policy" url={PRIVACY_URL} />
          {SUPPORT_URL !== null && (
            <>
              <Divider />
              <LinkRow label="Support" url={SUPPORT_URL} />
            </>
          )}
          {DELETION_URL !== null && (
            <>
              <Divider />
              <LinkRow label="Request Account Deletion" url={DELETION_URL} destructive />
            </>
          )}
        </View>
      </View>

      <View style={styles.section}>
        <View style={styles.card}>
          <Row label="Version" value={`OpHalo Keep ${APP_VERSION}`} />
        </View>
      </View>

      <TouchableOpacity style={styles.signOutButton} onPress={handleLogout}>
        <Text style={styles.signOutText}>Sign Out</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue} numberOfLines={1}>{value}</Text>
    </View>
  );
}

function LinkRow({ label, url, destructive = false }: { label: string; url: string; destructive?: boolean }) {
  return (
    <TouchableOpacity
      style={styles.row}
      onPress={() => void WebBrowser.openBrowserAsync(url)}
      accessibilityRole="link"
    >
      <Text style={[styles.rowLabel, destructive && styles.destructiveText]}>{label}</Text>
      <Text style={styles.chevron}>›</Text>
    </TouchableOpacity>
  );
}

function Divider() {
  return <View style={styles.divider} />;
}

const styles = StyleSheet.create({
  container: { paddingVertical: 24, paddingHorizontal: 16 },
  section: { marginBottom: 24 },
  sectionHeader: {
    fontSize: 12,
    fontWeight: '600',
    opacity: 0.4,
    letterSpacing: 0.5,
    marginBottom: 6,
    marginLeft: 4,
  },
  card: {
    borderRadius: 10,
    overflow: 'hidden',
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: 'rgba(128,128,128,0.3)',
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 13,
  },
  rowLabel: { fontSize: 16 },
  rowValue: { fontSize: 16, opacity: 0.45, flexShrink: 1, marginLeft: 8 },
  chevron: { fontSize: 18, opacity: 0.3 },
  destructiveText: { color: '#cc0000' },
  divider: {
    height: StyleSheet.hairlineWidth,
    backgroundColor: 'rgba(128,128,128,0.3)',
    marginLeft: 16,
  },
  signOutButton: {
    borderWidth: 1,
    borderColor: '#cc0000',
    borderRadius: 10,
    paddingVertical: 14,
    alignItems: 'center',
  },
  signOutText: { color: '#cc0000', fontSize: 16, fontWeight: '500' },
});
