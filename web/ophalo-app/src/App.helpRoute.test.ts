import { describe, it, expect, afterEach } from "vitest";
import { getRouteFromLocation } from "./App";

// GAP-038 / BL149 (038-1b-i): the `#/help` content route grammar. No params; any unknown hash
// still falls back to the Requests route.
afterEach(() => {
  window.location.hash = "";
});

describe("getRouteFromLocation — #/help", () => {
  it("parses #/help to { page: 'help' }", () => {
    window.location.hash = "#/help";
    expect(getRouteFromLocation()).toEqual({ page: "help" });
  });

  it("ignores a query string on #/help", () => {
    window.location.hash = "#/help?ref=menu";
    expect(getRouteFromLocation()).toEqual({ page: "help" });
  });

  it("falls back to the requests route for an unknown hash", () => {
    window.location.hash = "#/not-a-real-route";
    expect(getRouteFromLocation()).toEqual({ page: "requests" });
  });
});
