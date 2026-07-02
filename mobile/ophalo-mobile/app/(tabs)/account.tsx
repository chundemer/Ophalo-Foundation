import { Alert, StyleSheet, TouchableOpacity } from 'react-native';

import { Text, View } from '@/components/Themed';
import { useAuth } from '@/src/auth/AuthContext';

export default function AccountScreen() {
  const { user, logout } = useAuth();

  function handleLogout() {
    Alert.alert('Sign out', 'Are you sure you want to sign out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Sign out',
        style: 'destructive',
        onPress: () => { void logout(); },
      },
    ]);
  }

  return (
    <View style={styles.container}>
      {user && (
        <>
          <Text style={styles.role}>{user.accountRole}</Text>
          <Text style={styles.id}>{user.accountUserId}</Text>
        </>
      )}
      <TouchableOpacity style={styles.button} onPress={handleLogout}>
        <Text style={styles.buttonText}>Sign out</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    paddingHorizontal: 32,
  },
  role: {
    fontSize: 18,
    fontWeight: '600',
    textTransform: 'capitalize',
  },
  id: {
    fontSize: 12,
    opacity: 0.5,
  },
  button: {
    marginTop: 24,
    borderWidth: 1,
    borderColor: '#cc0000',
    borderRadius: 8,
    paddingHorizontal: 24,
    paddingVertical: 12,
  },
  buttonText: {
    color: '#cc0000',
    fontSize: 16,
    fontWeight: '500',
  },
});
