import { useLocalSearchParams, useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet } from 'react-native';

import { Text, View } from '@/components/Themed';
import { api, ApiError } from '@/src/api/client';
import { useAuth } from '@/src/auth/AuthContext';

type RedeemResponse = {
  sessionToken: string;
  expiresAtUtc: string;
};

type Phase = 'redeeming' | 'error';

export default function AuthCallbackScreen() {
  const { code } = useLocalSearchParams<{ code: string }>();
  const { storeToken } = useAuth();
  const router = useRouter();
  const [phase, setPhase] = useState<Phase>('redeeming');
  const [errorMessage, setErrorMessage] = useState('');

  useEffect(() => {
    if (!code) {
      setErrorMessage('Invalid sign-in link.');
      setPhase('error');
      return;
    }
    void redeem(code);
  }, [code]);

  async function redeem(handoffCode: string) {
    try {
      const result = await api.post<RedeemResponse>(
        '/auth/mobile-handoff/redeem',
        { handoffCode },
        false,
      );
      await storeToken(result.sessionToken);
      router.replace('/');
    } catch (err) {
      const isExpiredOrInvalid =
        err instanceof ApiError && (err.status === 400 || err.status === 410);
      const isRoleBlocked =
        err instanceof Error && err.message === 'mobile_access_not_available';
      setErrorMessage(
        isExpiredOrInvalid
          ? 'This link has expired or already been used. Please request a new sign-in link.'
          : isRoleBlocked
            ? 'Mobile access is not available for your account role. Please contact your administrator.'
            : 'Sign-in failed. Please try again.',
      );
      setPhase('error');
    }
  }

  if (phase === 'redeeming') {
    return (
      <View style={styles.container}>
        <ActivityIndicator size="large" />
        <Text style={styles.message}>Signing you in…</Text>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.error}>{errorMessage}</Text>
      <Text style={styles.link} onPress={() => router.replace('/signin')}>
        Back to sign in
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    gap: 16,
  },
  message: { fontSize: 16 },
  error: {
    textAlign: 'center',
    fontSize: 15,
    lineHeight: 22,
  },
  link: {
    color: '#0057D9',
    fontSize: 15,
  },
});
