import { Redirect } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Alert, KeyboardAvoidingView, Platform, StyleSheet, TextInput, TouchableOpacity } from 'react-native';

import { Text, View } from '@/components/Themed';
import { api, ApiError } from '@/src/api/client';
import { useAuth } from '@/src/auth/AuthContext';

type Step = 'input' | 'sent';

export default function SignInScreen() {
  const { user } = useAuth();
  const [email, setEmail] = useState('');
  const [step, setStep] = useState<Step>('input');
  const [submitting, setSubmitting] = useState(false);

  if (user) return <Redirect href="/" />;

  async function handleSignIn() {
    const trimmed = email.trim();
    if (!trimmed) {
      Alert.alert('Email required', 'Please enter your email address.');
      return;
    }

    setSubmitting(true);
    try {
      await api.post('/auth/signin', { email: trimmed, clientHint: 'mobile' }, false);
      setStep('sent');
    } catch (err) {
      const message = err instanceof ApiError && err.status === 422
        ? 'No account found for that email.'
        : 'Something went wrong. Please try again.';
      Alert.alert('Sign in failed', message);
    } finally {
      setSubmitting(false);
    }
  }

  if (step === 'sent') {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Check your email</Text>
        <Text style={styles.body}>
          We sent a sign-in link to {email.trim()}. Open it on this device to continue.
        </Text>
        <TouchableOpacity onPress={() => setStep('input')}>
          <Text style={styles.link}>Use a different email</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <KeyboardAvoidingView
      style={styles.flex}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <View style={styles.container}>
        <Text style={styles.title}>OpHalo Keep</Text>
        <Text style={styles.label}>Email address</Text>
        <TextInput
          style={styles.input}
          value={email}
          onChangeText={setEmail}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="email-address"
          textContentType="emailAddress"
          returnKeyType="go"
          onSubmitEditing={handleSignIn}
          placeholder="you@example.com"
          editable={!submitting}
        />
        <TouchableOpacity
          style={[styles.button, submitting && styles.buttonDisabled]}
          onPress={handleSignIn}
          disabled={submitting}
        >
          {submitting
            ? <ActivityIndicator color="#fff" />
            : <Text style={styles.buttonText}>Send sign-in link</Text>}
        </TouchableOpacity>
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    gap: 12,
  },
  title: {
    fontSize: 24,
    fontWeight: '700',
    marginBottom: 8,
  },
  label: {
    alignSelf: 'stretch',
    fontSize: 14,
    fontWeight: '500',
  },
  input: {
    alignSelf: 'stretch',
    borderWidth: 1,
    borderColor: '#ccc',
    borderRadius: 8,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 16,
  },
  button: {
    alignSelf: 'stretch',
    backgroundColor: '#0057D9',
    borderRadius: 8,
    paddingVertical: 14,
    alignItems: 'center',
    marginTop: 4,
  },
  buttonDisabled: { opacity: 0.6 },
  buttonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: '600',
  },
  body: {
    textAlign: 'center',
    fontSize: 15,
    lineHeight: 22,
  },
  link: {
    color: '#0057D9',
    fontSize: 15,
    marginTop: 8,
  },
});
