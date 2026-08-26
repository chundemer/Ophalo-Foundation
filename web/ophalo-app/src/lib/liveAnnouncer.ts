// Framework-free polite-announcement emitter. Exists because a component that triggers a retry
// success can unmount in the very same React commit that would render its own announcement (see
// `PrimaryActionControl`/`ActualWorkComposer` retry-recovery paths) — a local `role="status"`
// region never reaches the DOM in that case. `LiveAnnouncerRegion` subscribes once at the app
// root, which outlives every such unmount.

type Listener = (message: string) => void;

const listeners = new Set<Listener>();

export function announcePolite(message: string) {
  for (const listener of listeners) listener(message);
}

export function subscribeLiveAnnouncer(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
