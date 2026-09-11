import { useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';

import {
  QuickCaptureDraftCoordinator,
  QuickCaptureDraftFields,
  StoredQuickCaptureDraft,
} from './quickCaptureDraft';

/**
 * Thin React binding over QuickCaptureDraftCoordinator: hydrates on mount (and whenever the
 * signed-in account changes), exposes the restored draft once, and schedules autosave on every
 * field change. `hydrated` gates the form so no edit is lost between mount and the initial
 * AsyncStorage read completing.
 */
export function useQuickCaptureDraft(accountUserId: string | null) {
  const [hydrated, setHydrated] = useState(false);
  const [restoredDraft, setRestoredDraft] = useState<StoredQuickCaptureDraft | null>(null);
  const latestFieldsRef = useRef<QuickCaptureDraftFields | null>(null);

  const coordinator = useMemo(
    () =>
      accountUserId
        ? new QuickCaptureDraftCoordinator({ storage: AsyncStorage, accountUserId })
        : null,
    [accountUserId],
  );

  useEffect(() => {
    let cancelled = false;
    setHydrated(false);
    setRestoredDraft(null);
    latestFieldsRef.current = null;

    if (!coordinator) {
      setHydrated(true);
      return;
    }

    coordinator.hydrate().then((draft) => {
      if (cancelled) return;
      setRestoredDraft(draft);
      setHydrated(true);
    });

    return () => {
      cancelled = true;
      coordinator.cancelPendingSave();
    };
  }, [coordinator]);

  // Best-effort flush on backgrounding. This mitigates ordinary backgrounding/app-switch but
  // cannot promise anything against an immediate OS process kill — no client code can.
  useEffect(() => {
    if (!coordinator) return;
    const sub = AppState.addEventListener('change', (state) => {
      if (state !== 'active' && hydrated && latestFieldsRef.current) {
        void coordinator.flush(latestFieldsRef.current);
      }
    });
    return () => sub.remove();
  }, [coordinator, hydrated]);

  function saveDraft(fields: QuickCaptureDraftFields): void {
    latestFieldsRef.current = fields;
    coordinator?.scheduleSave(fields);
  }

  /** Writes immediately, bypassing the debounce — for the "Close" side of the dirty-dismiss
   *  confirm, where the pending debounced write can't be allowed to land after we navigate away. */
  async function flushDraft(fields: QuickCaptureDraftFields): Promise<void> {
    latestFieldsRef.current = fields;
    coordinator?.cancelPendingSave();
    await coordinator?.flush(fields);
  }

  async function discardDraft(): Promise<void> {
    latestFieldsRef.current = null;
    setRestoredDraft(null);
    await coordinator?.discard();
  }

  return { hydrated, restoredDraft, saveDraft, flushDraft, discardDraft };
}
