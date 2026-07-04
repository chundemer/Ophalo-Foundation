import { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  TextInput,
  TouchableOpacity,
  useColorScheme,
} from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { Redirect, router } from 'expo-router';

import { useAuth } from '@/src/auth/AuthContext';

import { Text, View } from '@/components/Themed';
import { useNetworkState } from '@/src/hooks/useNetworkState';
import {
  normalizePhoneDigits,
  useCreateRequest,
  usePhoneLookup,
} from '@/src/hooks/useQuickCapture';

const SOURCE_OPTIONS: { label: string; value: string }[] = [
  { label: 'Phone call', value: 'phone' },
  { label: 'Voicemail', value: 'voicemail' },
  { label: 'Text', value: 'text' },
  { label: 'Email', value: 'email' },
  { label: 'Walk-in', value: 'walk_in' },
  { label: 'Referral', value: 'referral' },
  { label: 'Other', value: 'other' },
];

export default function CaptureModal() {
  const { user } = useAuth();
  if (!user) return <Redirect href="/signin" />;
  return <CaptureModalContent />;
}

function CaptureModalContent() {
  const colorScheme = useColorScheme();
  const isDark = colorScheme === 'dark';
  const { isOnline } = useNetworkState();

  const [phone, setPhone] = useState('');
  const [customerName, setCustomerName] = useState('');
  const [customerEmail, setCustomerEmail] = useState('');
  const [description, setDescription] = useState('');
  const [source, setSource] = useState('phone');
  const [lookupApplied, setLookupApplied] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const digits = normalizePhoneDigits(phone);
  const lookupReady = digits.length >= 7 && digits.length <= 15;

  const { data: lookup, isFetching: lookupFetching } = usePhoneLookup(phone);
  const createMutation = useCreateRequest();

  // Auto-fill name/email once per new lookup result when a known customer is found.
  useEffect(() => {
    if (lookup?.customer && !lookupApplied) {
      setCustomerName(lookup.customer.name);
      setCustomerEmail(lookup.customer.email ?? '');
      setLookupApplied(true);
    }
  }, [lookup, lookupApplied]);

  const inputBg = isDark ? '#2C2C2E' : '#F2F2F7';
  const inputColor = isDark ? '#FFFFFF' : '#000000';
  const placeholderColor = isDark ? '#8E8E93' : '#8E8E93';
  const borderColor = isDark ? '#3A3A3C' : '#C6C6C8';

  const canCreate =
    isOnline &&
    digits.length >= 7 &&
    customerName.trim().length > 0 &&
    description.trim().length > 0 &&
    !createMutation.isPending;

  async function handleCreate() {
    if (!canCreate) return;
    setCreateError(null);
    try {
      const result = await createMutation.mutateAsync({
        customerName: customerName.trim(),
        customerPhone: digits,
        customerEmail: customerEmail.trim() || null,
        description: description.trim(),
        source,
      });
      router.replace({ pathname: '/requests/[id]', params: { id: result.requestId } });
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Something went wrong. Please try again.';
      setCreateError(msg);
    }
  }

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <View style={[styles.header, { borderBottomColor: borderColor }]}>
        <TouchableOpacity onPress={() => router.back()} accessibilityLabel="Cancel">
          <Text style={styles.cancel}>Cancel</Text>
        </TouchableOpacity>
        <Text style={styles.title}>Quick Capture</Text>
        <TouchableOpacity
          onPress={handleCreate}
          disabled={!canCreate}
          accessibilityLabel="Save request"
        >
          {createMutation.isPending ? (
            <ActivityIndicator size="small" />
          ) : (
            <Text style={[styles.save, !canCreate && styles.saveDisabled]}>Save</Text>
          )}
        </TouchableOpacity>
      </View>

      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.scrollContent}
        keyboardShouldPersistTaps="handled"
      >
        {!isOnline && (
          <View style={styles.offlineBanner}>
            <Text style={styles.offlineText}>No connection — save disabled until online.</Text>
          </View>
        )}

        {createError && (
          <View style={styles.errorBanner}>
            <Text style={styles.errorText}>{createError}</Text>
          </View>
        )}

        <Text style={styles.label}>Phone *</Text>
        <View style={[styles.inputRow, { backgroundColor: inputBg, borderColor }]}>
          <TextInput
            style={[styles.input, { color: inputColor }]}
            value={phone}
            onChangeText={(t) => {
              setPhone(t);
              if (lookupApplied) {
                // Clear auto-filled fields so the next lookup can re-fill cleanly.
                setCustomerName('');
                setCustomerEmail('');
              }
              setLookupApplied(false);
              setCreateError(null);
            }}
            placeholder="Customer phone number"
            placeholderTextColor={placeholderColor}
            keyboardType="phone-pad"
            autoFocus
            returnKeyType="next"
            accessibilityLabel="Customer phone number"
          />
          {lookupFetching && <ActivityIndicator size="small" style={styles.lookupSpinner} />}
        </View>

        {lookup && lookupReady && (
          <View style={styles.lookupResult}>
            {lookup.customer ? (
              <>
                <Text style={styles.lookupFound}>
                  Known customer: {lookup.customer.name}
                </Text>
                {lookup.activeRequests.length > 0 && (
                  <View style={styles.activeRequests}>
                    <Text style={styles.activeRequestsLabel}>
                      {lookup.activeRequests.length} active request
                      {lookup.activeRequests.length !== 1 ? 's' : ''}
                      {lookup.hasMoreActiveRequests ? '+' : ''}
                    </Text>
                    {lookup.activeRequests.map((r) => (
                      <Text key={r.requestId} style={styles.activeRequestRow}>
                        {r.referenceCode} · {r.status} · {r.description.slice(0, 60)}
                        {r.description.length > 60 ? '…' : ''}
                      </Text>
                    ))}
                  </View>
                )}
              </>
            ) : (
              <Text style={styles.lookupNew}>New customer</Text>
            )}
          </View>
        )}

        <Text style={styles.label}>Name *</Text>
        <TextInput
          style={[styles.inputStandalone, { backgroundColor: inputBg, borderColor, color: inputColor }]}
          value={customerName}
          onChangeText={setCustomerName}
          placeholder="Customer name"
          placeholderTextColor={placeholderColor}
          returnKeyType="next"
          accessibilityLabel="Customer name"
        />

        <Text style={styles.label}>Email</Text>
        <TextInput
          style={[styles.inputStandalone, { backgroundColor: inputBg, borderColor, color: inputColor }]}
          value={customerEmail}
          onChangeText={setCustomerEmail}
          placeholder="Customer email (optional)"
          placeholderTextColor={placeholderColor}
          keyboardType="email-address"
          autoCapitalize="none"
          returnKeyType="next"
          accessibilityLabel="Customer email"
        />

        <Text style={styles.label}>Description *</Text>
        <TextInput
          style={[styles.inputStandalone, styles.descriptionInput, { backgroundColor: inputBg, borderColor, color: inputColor }]}
          value={description}
          onChangeText={setDescription}
          placeholder="What does the customer need?"
          placeholderTextColor={placeholderColor}
          multiline
          returnKeyType="default"
          accessibilityLabel="Request description"
        />

        <Text style={styles.label}>Source</Text>
        <View style={styles.sourceGrid}>
          {SOURCE_OPTIONS.map((opt) => (
            <TouchableOpacity
              key={opt.value}
              style={[
                styles.sourceChip,
                { borderColor },
                source === opt.value && styles.sourceChipSelected,
              ]}
              onPress={() => setSource(opt.value)}
              accessibilityLabel={opt.label}
              accessibilityRole="radio"
              accessibilityState={{ selected: source === opt.value }}
            >
              <Text
                style={[
                  styles.sourceChipText,
                  source === opt.value && styles.sourceChipTextSelected,
                ]}
              >
                {opt.label}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
      </ScrollView>
      <StatusBar style={Platform.OS === 'ios' ? 'light' : 'auto'} />
    </KeyboardAvoidingView>
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
  },
  title: { fontSize: 17, fontWeight: '600' },
  cancel: { fontSize: 17, color: '#007AFF' },
  save: { fontSize: 17, fontWeight: '600', color: '#007AFF' },
  saveDisabled: { opacity: 0.4 },
  scroll: { flex: 1 },
  scrollContent: { padding: 20, paddingBottom: 48 },
  offlineBanner: {
    backgroundColor: '#FFF3CD',
    borderRadius: 8,
    padding: 12,
    marginBottom: 16,
  },
  offlineText: { fontSize: 14, color: '#856404' },
  errorBanner: {
    backgroundColor: '#F8D7DA',
    borderRadius: 8,
    padding: 12,
    marginBottom: 16,
  },
  errorText: { fontSize: 14, color: '#721C24' },
  label: { fontSize: 13, fontWeight: '600', marginBottom: 6, marginTop: 16, opacity: 0.6, textTransform: 'uppercase', letterSpacing: 0.5 },
  inputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    paddingHorizontal: 12,
  },
  input: { flex: 1, fontSize: 16, paddingVertical: 12 },
  lookupSpinner: { marginLeft: 8 },
  inputStandalone: {
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    paddingHorizontal: 12,
    paddingVertical: 12,
    fontSize: 16,
  },
  descriptionInput: { minHeight: 88, textAlignVertical: 'top' },
  lookupResult: { marginTop: 8, marginBottom: 4 },
  lookupFound: { fontSize: 14, fontWeight: '600', color: '#168A9A' },
  lookupNew: { fontSize: 14, opacity: 0.5 },
  activeRequests: { marginTop: 6 },
  activeRequestsLabel: { fontSize: 13, opacity: 0.6, marginBottom: 2 },
  activeRequestRow: { fontSize: 13, opacity: 0.5, marginTop: 2 },
  sourceGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
    marginTop: 4,
  },
  sourceChip: {
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 20,
    borderWidth: 1,
  },
  sourceChipSelected: { backgroundColor: '#168A9A', borderColor: '#168A9A' },
  sourceChipText: { fontSize: 14 },
  sourceChipTextSelected: { color: '#FFFFFF', fontWeight: '600' },
});
