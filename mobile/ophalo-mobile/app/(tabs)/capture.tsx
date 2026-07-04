import { Redirect } from 'expo-router';

// Capture tab file required by Expo Router file-based routing.
// The tab bar button intercepts all presses and opens the capture modal;
// this screen is never shown. S17f replaces with the real Quick Capture form.
export default function CaptureScreen() {
  return <Redirect href="/(tabs)" />;
}
