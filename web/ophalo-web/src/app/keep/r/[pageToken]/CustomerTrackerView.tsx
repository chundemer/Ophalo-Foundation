"use client";

import { useEffect, useRef, useState } from "react";
import {
  KeepBusinessHeader,
  KeepPageFooter,
} from "@/components/keep/KeepPublicShell";
import {
  type CustomerPageData,
  type ComposerPhase,
  PRIMARY_ACTION,
  SECONDARY_ACTIONS,
  trackerCanvasStyle,
  businessInitials,
  parseErrorCode,
  errorMessageForCode,
} from "./tracker-types";
import { TrackerExpiredView } from "./TrackerExpiredView";
import { TrackerStatusCard } from "./TrackerStatusCard";
import { TrackerActionCard } from "./TrackerActionCard";
import { TrackerInitialRequestCard } from "./TrackerInitialRequestCard";
import { TrackerHistoryCard } from "./TrackerHistoryCard";

export type { CustomerPageData };

export function CustomerTrackerView({
  initialPage,
  pageToken,
}: {
  initialPage: CustomerPageData;
  pageToken: string;
}) {
  const [page, setPage] = useState(initialPage);
  const [phase, setPhase] = useState<ComposerPhase>({ kind: "idle" });
  const [message, setMessage] = useState("");
  const [wasResolved, setWasResolved] = useState<boolean | null>(null);
  const [comment, setComment] = useState("");
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);
  const [copied, setCopied] = useState(false);
  const [canSharePage, setCanSharePage] = useState(false);
  const dismissTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
  const events = [...(page.events ?? [])].reverse();
  const allowedActions = page.allowedActions ?? [];

  const hasPrimaryAction = allowedActions.includes(PRIMARY_ACTION);
  const availableSecondaryActions = (SECONDARY_ACTIONS as readonly string[]).filter(
    (a) => allowedActions.includes(a)
  );
  const hasCancellationAction = allowedActions.includes("cancellation_requested");
  const hasFeedbackAction = allowedActions.includes("feedback");
  const hasActions =
    hasPrimaryAction ||
    availableSecondaryActions.length > 0 ||
    hasCancellationAction ||
    hasFeedbackAction;

  const selectedAction =
    phase.kind === "composing" || phase.kind === "submitting" ? phase.action : null;
  const isSubmitting =
    phase.kind === "submitting" || phase.kind === "submitting_feedback";

  const latestBusinessUpdate =
    (page.events ?? [])
      .slice()
      .reverse()
      .find((e) => e.actorLabel === "business" && e.content !== null) ?? null;

  const initials = businessInitials(page.businessName);

  useEffect(() => {
    setCanSharePage(typeof navigator !== "undefined" && typeof navigator.share === "function");
  }, []);

  useEffect(() => {
    if (phase.kind === "sent" || phase.kind === "feedback_sent") {
      dismissTimer.current = setTimeout(() => setPhase({ kind: "idle" }), 5000);
    }
    return () => { if (dismissTimer.current) clearTimeout(dismissTimer.current); };
  }, [phase.kind]);

  async function shareOrCopyLink() {
    const url = window.location.href;
    if (navigator.share) {
      try {
        await navigator.share({
          title: `${page.businessName} request`,
          text: `View your request with ${page.businessName}.`,
          url,
        });
        return;
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") return;
      }
    }
    await navigator.clipboard.writeText(url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2000);
  }

  function openAction(action: string) {
    setPhase({ kind: "composing", action });
    setErrorMsg(null);
  }

  function backToIdle() {
    setPhase({ kind: "idle" });
    setErrorMsg(null);
  }

  function dismissSent() {
    if (dismissTimer.current) clearTimeout(dismissTimer.current);
    setPhase({ kind: "idle" });
  }

  async function submitMessage(action: string) {
    const trimmed = message.trim();
    if (!trimmed) { setErrorMsg("Message can't be empty."); return; }
    setErrorMsg(null);
    setPhase({ kind: "submitting", action });
    try {
      const res = await fetch(
        `${apiBase}/keep/r/${encodeURIComponent(pageToken)}/${action}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Keep-Request-Version": page.version ?? "",
          },
          body: JSON.stringify({ message: trimmed }),
        }
      );
      if (res.ok) {
        const updated: unknown = await res.json().catch(() => null);
        if (updated != null && typeof updated === "object") setPage(updated as CustomerPageData);
        setMessage("");
        setPhase({ kind: "sent" });
        return;
      }
      if (res.status === 410) { setExpired(true); return; }
      const code = await parseErrorCode(res);
      setErrorMsg(errorMessageForCode(code, res.status));
      setPhase({ kind: "composing", action });
    } catch {
      setErrorMsg("…check your connection and try again.");
      setPhase({ kind: "composing", action });
    }
  }

  async function submitFeedback() {
    if (wasResolved === null) {
      setErrorMsg("Please indicate whether your request was resolved.");
      return;
    }
    setErrorMsg(null);
    setPhase({ kind: "submitting_feedback" });
    try {
      const res = await fetch(
        `${apiBase}/keep/r/${encodeURIComponent(pageToken)}/feedback`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Keep-Request-Version": page.version ?? "",
          },
          body: JSON.stringify({ wasResolved, comment: comment.trim() || null }),
        }
      );
      if (res.ok) {
        const updated: unknown = await res.json().catch(() => null);
        if (updated != null && typeof updated === "object") setPage(updated as CustomerPageData);
        setComment("");
        setWasResolved(null);
        setPhase({ kind: "feedback_sent" });
        return;
      }
      if (res.status === 410) { setExpired(true); return; }
      const code = await parseErrorCode(res);
      setErrorMsg(errorMessageForCode(code, res.status));
      setPhase({ kind: "feedback" });
    } catch {
      setErrorMsg("…check your connection and try again.");
      setPhase({ kind: "feedback" });
    }
  }

  if (expired) {
    return (
      <TrackerExpiredView
        businessName={page.businessName}
        referenceCode={page.referenceCode}
      />
    );
  }

  return (
    <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
      <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">

        <KeepBusinessHeader
          businessName={page.businessName}
          label="Private Request Page"
          description={`This private page keeps your request details and updates from ${page.businessName} in one place.`}
          className="pb-1"
        />

        <TrackerStatusCard
          status={page.status}
          origin={page.origin}
          businessName={page.businessName}
          referenceCode={page.referenceCode}
          latestBusinessUpdate={latestBusinessUpdate}
          copied={copied}
          canSharePage={canSharePage}
          onShareOrCopy={shareOrCopyLink}
        />

        {hasActions && (
          <TrackerActionCard
            phase={phase}
            businessName={page.businessName}
            message={message}
            onMessageChange={setMessage}
            comment={comment}
            onCommentChange={setComment}
            wasResolved={wasResolved}
            onWasResolvedChange={setWasResolved}
            errorMsg={errorMsg}
            isSubmitting={isSubmitting}
            selectedAction={selectedAction}
            hasPrimaryAction={hasPrimaryAction}
            availableSecondaryActions={availableSecondaryActions}
            hasCancellationAction={hasCancellationAction}
            hasFeedbackAction={hasFeedbackAction}
            onOpenAction={openAction}
            onBackToIdle={backToIdle}
            onSubmitMessage={submitMessage}
            onSubmitFeedback={submitFeedback}
            onDismissSent={dismissSent}
          />
        )}

        {(page.description || page.intakeUrgency) && (
          <TrackerInitialRequestCard
            description={page.description}
            intakeUrgency={page.intakeUrgency}
            businessName={page.businessName}
          />
        )}

        {page.events !== null && (
          <TrackerHistoryCard
            events={events}
            referenceCode={page.referenceCode}
            businessName={page.businessName}
            initials={initials}
            latestBusinessUpdate={latestBusinessUpdate}
          />
        )}

      </div>

      <KeepPageFooter className="mx-auto w-full max-w-2xl" />
    </main>
  );
}
