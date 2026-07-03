import * as Crypto from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';

const SESSION_TOKEN_KEY = 'ophalo_session_token';
const INSTALLATION_ID_KEY = 'ophalo_installation_id';

export async function getSessionToken(): Promise<string | null> {
  return SecureStore.getItemAsync(SESSION_TOKEN_KEY);
}

export async function setSessionToken(token: string): Promise<void> {
  await SecureStore.setItemAsync(SESSION_TOKEN_KEY, token);
}

export async function clearSessionToken(): Promise<void> {
  await SecureStore.deleteItemAsync(SESSION_TOKEN_KEY);
}

export async function getAppInstallationId(): Promise<string> {
  const existing = await SecureStore.getItemAsync(INSTALLATION_ID_KEY);
  if (existing) return existing;
  const id = Crypto.randomUUID();
  await SecureStore.setItemAsync(INSTALLATION_ID_KEY, id);
  return id;
}
