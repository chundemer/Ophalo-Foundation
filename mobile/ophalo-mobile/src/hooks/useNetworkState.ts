import { useEffect, useState } from 'react';
import NetInfo from '@react-native-community/netinfo';

export function useNetworkState(): { isOnline: boolean } {
  const [isOnline, setIsOnline] = useState(true);

  useEffect(() => {
    const unsubscribe = NetInfo.addEventListener((state) => {
      // Only block when clearly offline; treat unknown/null as online.
      if (state.isConnected === false) {
        setIsOnline(false);
      } else {
        setIsOnline(true);
      }
    });
    return unsubscribe;
  }, []);

  return { isOnline };
}
