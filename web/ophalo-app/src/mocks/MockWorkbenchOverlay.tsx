import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { AccountRole } from "../lib/apiClient";
import { currentMockRole, setMockRole } from "./mockState";

const ROLES: { id: AccountRole; label: string }[] = [
  { id: "owner", label: "Owner" },
  { id: "admin", label: "Admin" },
  { id: "operator", label: "Operator" },
  { id: "viewer", label: "Viewer" },
];

export function MockWorkbenchOverlay() {
  const queryClient = useQueryClient();
  const [role, setRole] = useState<AccountRole>(currentMockRole);

  function handleRoleChange(newRole: AccountRole) {
    setMockRole(newRole);
    setRole(newRole);
    void queryClient.invalidateQueries();
  }

  return (
    <div className="fixed bottom-4 left-4 z-50 flex items-center gap-1 rounded-full bg-slate-900/90 px-3 py-1.5 text-xs font-mono shadow-lg backdrop-blur-sm select-none">
      <span className="text-slate-400 mr-1.5 font-sans text-[10px] uppercase tracking-wider">mock</span>
      {ROLES.map((r) => (
        <button
          key={r.id}
          type="button"
          onClick={() => handleRoleChange(r.id)}
          className={`rounded px-2 py-0.5 transition-colors cursor-pointer ${
            role === r.id
              ? "bg-white text-slate-900 font-semibold"
              : "text-slate-400 hover:text-white"
          }`}
        >
          {r.label}
        </button>
      ))}
    </div>
  );
}
