import { useEffect, useRef, useState } from "react";
import { subscribeLiveAnnouncer } from "../../lib/liveAnnouncer";

const CLEAR_AFTER_MS = 5000;

/**
 * Mounted once at the app root (`App.tsx`) so it outlives any component that triggers an
 * announcement and then unmounts in the same commit — see `liveAnnouncer.ts`. Never receives
 * focus; `role="status"`/`aria-live="polite"` announce without moving it.
 */
export function LiveAnnouncerRegion() {
  const [message, setMessage] = useState("");
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const frameRef = useRef<number | null>(null);

  useEffect(() => {
    const unsubscribe = subscribeLiveAnnouncer((next) => {
      if (timerRef.current) clearTimeout(timerRef.current);
      if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
      // Clear first so a repeated identical message still re-triggers the live region (most
      // screen readers do not re-announce unchanged text).
      setMessage("");
      frameRef.current = requestAnimationFrame(() => {
        frameRef.current = null;
        setMessage(next);
      });
      timerRef.current = setTimeout(() => setMessage(""), CLEAR_AFTER_MS);
    });
    return () => {
      unsubscribe();
      if (timerRef.current) clearTimeout(timerRef.current);
      if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
    };
  }, []);

  return (
    <div role="status" aria-live="polite" className="sr-only">
      {message}
    </div>
  );
}
