import { describe, it, expect } from "vitest";
import { getNavItems } from "./App";

describe("getNavItems", () => {
  it("operator sees only Requests, regardless of entitlement", () => {
    const ids = getNavItems("operator", true).map((i) => i.id);
    expect(ids).toEqual(["requests"]);
  });

  it("viewer sees only Requests, regardless of entitlement", () => {
    const ids = getNavItems("viewer", true).map((i) => i.id);
    expect(ids).toEqual(["requests"]);
  });

  it("owner without the Price Book entitlement does not see Price Book", () => {
    const ids = getNavItems("owner", false).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "settings"]);
  });

  it("admin without the Price Book entitlement does not see Price Book", () => {
    const ids = getNavItems("admin", false).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "settings"]);
  });

  it("owner with the Price Book entitlement sees it between Getting Started and Settings", () => {
    const ids = getNavItems("owner", true).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "pricebook", "settings"]);
  });

  it("admin with the Price Book entitlement sees it", () => {
    const ids = getNavItems("admin", true).map((i) => i.id);
    expect(ids).toEqual(["requests", "home", "pricebook", "settings"]);
  });
});
