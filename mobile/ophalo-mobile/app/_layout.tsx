import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query';
import { useFonts } from 'expo-font';
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { useEffect } from 'react';
import { AppState, AppStateStatus, StyleSheet, View } from 'react-native';
import 'react-native-reanimated';

import { Text } from '@/components/Themed';
import { useColorScheme } from '@/components/useColorScheme';
import { AuthProvider, useAuth } from '@/src/auth/AuthContext';

const queryClient = new QueryClient();

// React Native does not fire browser-style focus events, so wire AppState → focusManager
// so TanStack Query refetches stale queries when the app returns to the foreground.
focusManager.setEventListener((handleFocus) => {
  const subscription = AppState.addEventListener('change', (state: AppStateStatus) => {
    handleFocus(state === 'active');
  });
  return () => subscription.remove();
});

export { ErrorBoundary } from 'expo-router';

export const unstable_settings = {
  initialRouteName: '(tabs)',
};

SplashScreen.preventAutoHideAsync();

export default function RootLayout() {
  const [loaded, error] = useFonts({
    SpaceMono: require('../assets/fonts/SpaceMono-Regular.ttf'),
  });

  useEffect(() => {
    if (error) throw error;
  }, [error]);

  useEffect(() => {
    if (loaded) {
      SplashScreen.hideAsync();
    }
  }, [loaded]);

  if (!loaded) {
    return null;
  }

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RootLayoutNav />
      </AuthProvider>
    </QueryClientProvider>
  );
}

function RootLayoutNav() {
  const colorScheme = useColorScheme();
  const { isLoading, isRoleBlocked } = useAuth();

  if (isLoading) {
    return <View style={{ flex: 1 }} />;
  }

  if (isRoleBlocked) {
    return (
      <View style={styles.blocked}>
        <Text style={styles.blockedTitle}>Access Not Available</Text>
        <Text style={styles.blockedBody}>
          OpHalo Keep mobile is not available for your account role.
          Please contact your account administrator.
        </Text>
      </View>
    );
  }

  return (
    <ThemeProvider value={colorScheme === 'dark' ? DarkTheme : DefaultTheme}>
      <Stack>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="signin" options={{ headerShown: false }} />
        <Stack.Screen name="auth/callback" options={{ headerShown: false }} />
        <Stack.Screen name="modal" options={{ presentation: 'modal' }} />
      </Stack>
    </ThemeProvider>
  );
}

const styles = StyleSheet.create({
  blocked: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    gap: 12,
  },
  blockedTitle: { fontSize: 18, fontWeight: '600' },
  blockedBody: { fontSize: 15, textAlign: 'center', lineHeight: 22 },
});
