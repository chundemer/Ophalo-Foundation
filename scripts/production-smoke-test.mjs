#!/usr/bin/env node
// GAP-039c / session 0.5 — repository-owned production smoke test.
// Usage and modes are documented in docs/runbook/production-smoke-test.md.
// Requires Node 20+ (built-in fetch). No dependencies.

const COOKIE_NAME = "ophalo.sid";

function readArg(name) {
  const prefix = `--${name}=`;
  const found = process.argv.slice(2).find((a) => a.startsWith(prefix));
  return found ? found.slice(prefix.length) : undefined;
}

const baseUrl = process.env.SMOKE_API_BASE_URL?.replace(/\/+$/, "");
const email = process.env.SMOKE_ACCOUNT_EMAIL;
const storedCookie = process.env.SMOKE_SESSION_COOKIE;
const exchangeCode = readArg("exchange-code");

if (!baseUrl || !email) {
  console.error(
    "SMOKE_API_BASE_URL and SMOKE_ACCOUNT_EMAIL are required. See docs/runbook/production-smoke-test.md.",
  );
  process.exit(2);
}

const results = [];

function record(name, status, detail) {
  results.push({ name, status });
  const icon = status === "pass" ? "✓" : status === "skip" ? "–" : "✗";
  console.log(`${icon} ${name}${detail ? ` — ${detail}` : ""}`);
}

async function checkHealthLive() {
  try {
    const res = await fetch(`${baseUrl}/health/live`);
    res.ok ? record("health/live", "pass") : record("health/live", "fail", `HTTP ${res.status}`);
  } catch (err) {
    record("health/live", "fail", String(err));
  }
}

async function checkHealthReady() {
  try {
    const res = await fetch(`${baseUrl}/health/ready`);
    res.ok ? record("health/ready", "pass") : record("health/ready", "fail", `HTTP ${res.status}`);
  } catch (err) {
    record("health/ready", "fail", String(err));
  }
}

async function triggerSignIn() {
  try {
    const res = await fetch(`${baseUrl}/auth/signin`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });
    res.ok
      ? record("auth/signin (trigger)", "pass")
      : record("auth/signin (trigger)", "fail", `HTTP ${res.status}`);
  } catch (err) {
    record("auth/signin (trigger)", "fail", String(err));
  }
}

function extractSessionCookie(res) {
  const all =
    typeof res.headers.getSetCookie === "function"
      ? res.headers.getSetCookie()
      : [res.headers.get("set-cookie")].filter(Boolean);
  for (const raw of all) {
    const match = raw.match(new RegExp(`^${COOKIE_NAME}=([^;]+)`));
    if (match) return match[1];
  }
  return null;
}

// Full end-to-end mode only: exchanges a real code copied from the smoke inbox's
// magic-link email, proving delivery + exchange still work (not run by default).
async function exchangeCodeForCookie(code) {
  try {
    const res = await fetch(`${baseUrl}/auth/exchange`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code, clientType: "browser" }),
    });
    if (!res.ok) {
      record("auth/exchange (full end-to-end)", "fail", `HTTP ${res.status}`);
      return null;
    }
    const cookie = extractSessionCookie(res);
    if (!cookie) {
      record("auth/exchange (full end-to-end)", "fail", "no session cookie in response");
      return null;
    }
    record("auth/exchange (full end-to-end)", "pass");
    return cookie;
  } catch (err) {
    record("auth/exchange (full end-to-end)", "fail", String(err));
    return null;
  }
}

async function checkAuthMe(cookieValue) {
  try {
    const res = await fetch(`${baseUrl}/auth/me`, {
      headers: { Cookie: `${COOKIE_NAME}=${cookieValue}` },
    });
    if (!res.ok) return record("auth/me", "fail", `HTTP ${res.status}`);
    const body = await res.json();
    body.isAuthenticated
      ? record("auth/me", "pass", `role=${body.accountRole}`)
      : record("auth/me", "fail", "isAuthenticated=false");
  } catch (err) {
    record("auth/me", "fail", String(err));
  }
}

async function checkRequestList(cookieValue) {
  try {
    const res = await fetch(`${baseUrl}/keep/requests`, {
      headers: { Cookie: `${COOKIE_NAME}=${cookieValue}` },
    });
    if (!res.ok) return record("keep/requests (request-list load)", "fail", `HTTP ${res.status}`);
    const body = await res.json();
    Array.isArray(body.requests)
      ? record("keep/requests (request-list load)", "pass", `${body.requests.length} row(s)`)
      : record("keep/requests (request-list load)", "fail", "unexpected response shape");
  } catch (err) {
    record("keep/requests (request-list load)", "fail", String(err));
  }
}

async function main() {
  console.log(`Production smoke test — ${baseUrl}\n`);

  await checkHealthLive();
  await checkHealthReady();

  let sessionCookie = storedCookie ?? null;

  if (exchangeCode) {
    // Full end-to-end mode: a POST to /auth/signin invalidates the previous unused
    // sign-in code for this account (D8/AccountAuthCode single-active-code contract),
    // so it must NOT run here — the supplied code came from an earlier, separate
    // sign-in trigger (see docs/runbook/production-smoke-test.md).
    record(
      "auth/signin (trigger)",
      "skip",
      "skipped in exchange-code mode — would invalidate the supplied code",
    );
    const freshCookie = await exchangeCodeForCookie(exchangeCode);
    if (freshCookie) sessionCookie = freshCookie;
  } else {
    await triggerSignIn();
  }

  if (sessionCookie) {
    await checkAuthMe(sessionCookie);
    await checkRequestList(sessionCookie);
  } else {
    const reason = "no session available — set SMOKE_SESSION_COOKIE or pass --exchange-code=<code>";
    record("auth/me", "skip", reason);
    record("keep/requests (request-list load)", "skip", reason);
  }

  const failed = results.filter((r) => r.status === "fail");
  const skipped = results.filter((r) => r.status === "skip");
  const passed = results.length - failed.length - skipped.length;

  console.log(`\n${passed}/${results.length} passed, ${skipped.length} skipped, ${failed.length} failed`);
  process.exit(failed.length > 0 ? 1 : 0);
}

main();
