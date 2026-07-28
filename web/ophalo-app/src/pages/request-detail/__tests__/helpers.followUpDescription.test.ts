import { describe, it, expect } from "vitest";
import { buildFollowUpDescription } from "../helpers";
import { DESCRIPTION_MAX_LENGTH } from "../../../components/quick-capture/utils";

// GAP-049: a follow-up prefill built from a near-limit closed-request description must stay
// within the backend's 4000-char cap, truncating only the copied original text — never the
// provenance prefix — at a safe whitespace boundary, with a visible ellipsis when cut.

const PREFIX = "Follow-up to closed request KEEP-1234: ";

describe("buildFollowUpDescription (GAP-049)", () => {
  it("returns the full prefix + text unchanged when it fits", () => {
    const result = buildFollowUpDescription(PREFIX, "Leaky faucet in the upstairs bathroom.");
    expect(result).toEqual({
      description: PREFIX + "Leaky faucet in the upstairs bathroom.",
      wasTruncated: false,
    });
  });

  it("truncates only the original text at a whitespace boundary and flags wasTruncated", () => {
    const original = "a".repeat(50) + " " + "b".repeat(DESCRIPTION_MAX_LENGTH);
    const { description, wasTruncated } = buildFollowUpDescription(PREFIX, original);

    expect(wasTruncated).toBe(true);
    expect(description.startsWith(PREFIX)).toBe(true);
    expect(description.length).toBeLessThanOrEqual(DESCRIPTION_MAX_LENGTH);
    expect(description.endsWith("…")).toBe(true);
    // Cut lands on the whitespace boundary, not mid "b" run.
    expect(description).toBe(PREFIX + "a".repeat(50) + "…");
  });

  it("hard-cuts when there is no whitespace boundary within the available room", () => {
    const original = "x".repeat(DESCRIPTION_MAX_LENGTH);
    const { description, wasTruncated } = buildFollowUpDescription(PREFIX, original);

    expect(wasTruncated).toBe(true);
    expect(description.length).toBeLessThanOrEqual(DESCRIPTION_MAX_LENGTH);
    expect(description.endsWith("…")).toBe(true);
  });

  it("never alters the original closed-request text itself — only the built prefill copy", () => {
    const original = "y".repeat(DESCRIPTION_MAX_LENGTH);
    buildFollowUpDescription(PREFIX, original);
    expect(original).toBe("y".repeat(DESCRIPTION_MAX_LENGTH));
  });
});
