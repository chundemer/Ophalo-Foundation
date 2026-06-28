import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, Circle, AlertCircle } from "lucide-react";
import { api, ApiError, type OnboardingChecklist } from "../lib/apiClient";
import { AccessLimited } from "./AccessLimited";

interface ChecklistItem {
  label: string;
  done: boolean;
}

function toItems(c: OnboardingChecklist): ChecklistItem[] {
  return [
    { label: "Save business profile and contact info", done: c.profileAndContactSaved },
    { label: "Set time zone", done: c.timezoneSaved },
    { label: "Configure response policy", done: c.policySaved },
    { label: "Activate intake link", done: c.intakeLinkActive },
    { label: "Invite a team member", done: c.operatorInvited },
    { label: "Register a mobile device", done: c.mobileDeviceRegistered },
    { label: "Receive first request", done: c.firstRequestCreated },
    { label: "Complete Quick Capture exercise", done: c.quickCaptureExerciseDone },
    { label: "Review tracker", done: c.trackerReviewDone },
    { label: "Learn spam classification", done: c.spamClassificationExplained },
  ];
}

function ChecklistView({ data }: { data: OnboardingChecklist }) {
  const items = toItems(data);
  const completedCount = items.filter((i) => i.done).length;

  return (
    <div className="min-h-screen bg-slate-50 py-12 px-4">
      <div className="mx-auto max-w-lg">
        <h1 className="font-serif text-2xl font-semibold text-slate-900 mb-1">
          Getting started
        </h1>
        <p className="text-slate-500 text-sm mb-8">
          {completedCount} of {items.length} steps complete
        </p>

        <ul className="space-y-3">
          {items.map(({ label, done }) => (
            <li
              key={label}
              className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3"
            >
              {done ? (
                <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-500" />
              ) : (
                <Circle className="h-5 w-5 shrink-0 text-slate-300" />
              )}
              <span
                className={`text-sm ${done ? "text-slate-400 line-through" : "text-slate-700"}`}
              >
                {label}
              </span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function CommercialBlock() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50">
      <div className="max-w-sm text-center px-6">
        <AlertCircle className="mx-auto mb-4 h-8 w-8 text-amber-400" />
        <h1 className="font-serif text-xl font-semibold text-slate-800 mb-2">
          Account access requires attention
        </h1>
        <p className="text-slate-500 text-sm leading-relaxed">
          Contact your account owner to restore access.
        </p>
      </div>
    </div>
  );
}

function GenericError() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50">
      <div className="max-w-sm text-center px-6">
        <AlertCircle className="mx-auto mb-4 h-8 w-8 text-slate-400" />
        <h1 className="font-serif text-xl font-semibold text-slate-800 mb-2">
          Something went wrong
        </h1>
        <p className="text-slate-500 text-sm leading-relaxed">
          Reload the page. If the problem persists, contact support.
        </p>
      </div>
    </div>
  );
}

export function Home() {
  const { data, isLoading, error } = useQuery({
    queryKey: ["onboarding"],
    queryFn: api.getOnboardingChecklist,
    retry: false,
  });

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <span className="text-slate-500 text-sm">Loading…</span>
      </div>
    );
  }

  if (error instanceof ApiError) {
    if (error.status === 403) return <AccessLimited />;
    if (error.status === 402) return <CommercialBlock />;
    return <GenericError />;
  }

  if (!data) return null;

  return <ChecklistView data={data} />;
}
