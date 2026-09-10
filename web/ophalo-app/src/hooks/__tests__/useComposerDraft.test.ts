import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import {
  useComposerDraft,
  seedComposerDraft,
  __resetComposerDrafts,
} from "../useComposerDraft";

beforeEach(() => {
  sessionStorage.clear();
  __resetComposerDrafts();
});

describe("useComposerDraft", () => {
  it("persists a field to sessionStorage and restores it after a store reset (fresh page load)", () => {
    const first = renderHook(() => useComposerDraft("req-1"));
    act(() => first.result.current.setField("message", "on our way"));

    expect(sessionStorage.getItem("keep:composer-draft:req-1")).toContain("on our way");

    first.unmount();
    __resetComposerDrafts();
    const reloaded = renderHook(() => useComposerDraft("req-1"));
    expect(reloaded.result.current.draft.message).toBe("on our way");
  });

  it("isolates drafts by requestId — request A's draft is never visible on request B", () => {
    const a = renderHook(() => useComposerDraft("req-A"));
    act(() => a.result.current.setField("message", "text for A"));

    const b = renderHook(() => useComposerDraft("req-B"));
    expect(b.result.current.draft.message).toBe("");

    // A still has its own draft.
    expect(a.result.current.draft.message).toBe("text for A");
  });

  it("two concurrent consumers of the same request share one draft object — neither clobbers the other's newer field", () => {
    const noteConsumer = renderHook(() => useComposerDraft("req-1"));
    const messageConsumer = renderHook(() => useComposerDraft("req-1"));

    act(() => noteConsumer.result.current.setField("note", "internal note text"));
    act(() => messageConsumer.result.current.setField("message", "customer message text"));

    // Both fields survive, seen from both consumers.
    for (const consumer of [noteConsumer, messageConsumer]) {
      expect(consumer.result.current.draft.note).toBe("internal note text");
      expect(consumer.result.current.draft.message).toBe("customer message text");
    }
    const stored = JSON.parse(sessionStorage.getItem("keep:composer-draft:req-1") ?? "{}");
    expect(stored).toMatchObject({ note: "internal note text", message: "customer message text" });
  });

  it("clearFields clears only the named fields and leaves the rest", () => {
    seedComposerDraft("req-1", { message: "msg", status: "scheduled", note: "keep me" });
    const { result } = renderHook(() => useComposerDraft("req-1"));

    act(() => result.current.clearFields(["message", "status"]));

    expect(result.current.draft.message).toBe("");
    expect(result.current.draft.status).toBe("");
    expect(result.current.draft.note).toBe("keep me");
  });

  it("removes the sessionStorage entry once every field is empty", () => {
    const { result } = renderHook(() => useComposerDraft("req-1"));
    act(() => result.current.setField("note", "x"));
    expect(sessionStorage.getItem("keep:composer-draft:req-1")).not.toBeNull();

    act(() => result.current.clearFields(["note"]));
    expect(sessionStorage.getItem("keep:composer-draft:req-1")).toBeNull();
  });
});
