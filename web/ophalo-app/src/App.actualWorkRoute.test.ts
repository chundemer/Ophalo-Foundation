import { describe, it, expect, afterEach } from "vitest";
import { getRouteFromLocation } from "./App";

// BL136 4f-i: the dedicated Actual Work Ticket Workspace route grammar,
// `#/request/:id/actual-work/:seg`, must be matched ahead of the generic `#/request/(.+)` detail
// pattern (whose `(.+)` would otherwise swallow the whole tail as the request id).
afterEach(() => {
  window.location.hash = "";
});

describe("getRouteFromLocation — actual-work workspace route", () => {
  it("parses the draft segment", () => {
    window.location.hash = "#/request/req-1/actual-work/draft";
    expect(getRouteFromLocation()).toEqual({ page: "actual-work", requestId: "req-1", visit: "draft" });
  });

  it("parses the new segment", () => {
    window.location.hash = "#/request/req-1/actual-work/new";
    expect(getRouteFromLocation()).toEqual({ page: "actual-work", requestId: "req-1", visit: "new" });
  });

  it("parses a submitted visit id segment", () => {
    window.location.hash = "#/request/req-1/actual-work/aw-42";
    expect(getRouteFromLocation()).toEqual({ page: "actual-work", requestId: "req-1", visit: "aw-42" });
  });

  it("still parses the bare request-detail route", () => {
    window.location.hash = "#/request/req-1";
    expect(getRouteFromLocation()).toEqual({ page: "detail", requestId: "req-1" });
  });
});
