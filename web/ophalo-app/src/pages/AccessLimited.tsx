import { ShieldOff } from "lucide-react";

export function AccessLimited() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50">
      <div className="max-w-sm text-center px-6">
        <ShieldOff className="mx-auto mb-4 h-8 w-8 text-slate-400" />
        <h1 className="font-serif text-xl font-semibold text-slate-800 mb-2">
          Access Limited
        </h1>
        <p className="text-slate-500 text-sm leading-relaxed">
          Your current role doesn't include setup and configuration. Contact
          your account owner to request access.
        </p>
      </div>
    </div>
  );
}
