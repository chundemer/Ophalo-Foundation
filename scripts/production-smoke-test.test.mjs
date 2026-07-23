// Regression coverage for scripts/production-smoke-test.mjs.
// Run with: node --test scripts/production-smoke-test.test.mjs
// Uses only Node's built-in test runner/assert/http — no dependencies, matching the
// script itself.

import test from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";
import path from "node:path";

const execFileAsync = promisify(execFile);
const scriptPath = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  "production-smoke-test.mjs",
);

function startMockServer() {
  const hits = { signin: 0, exchange: 0 };

  const server = createServer((req, res) => {
    const url = new URL(req.url, "http://localhost");

    if (req.method === "GET" && url.pathname === "/health/live") {
      res.writeHead(200);
      return res.end();
    }
    if (req.method === "GET" && url.pathname === "/health/ready") {
      res.writeHead(200);
      return res.end();
    }
    if (req.method === "POST" && url.pathname === "/auth/signin") {
      hits.signin += 1;
      res.writeHead(200);
      return res.end();
    }
    if (req.method === "POST" && url.pathname === "/auth/exchange") {
      hits.exchange += 1;
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        const parsed = JSON.parse(body);
        if (parsed.code !== "goodcode") {
          res.writeHead(422);
          return res.end();
        }
        res.writeHead(200, { "Set-Cookie": "ophalo.sid=freshcookie; HttpOnly; Path=/" });
        return res.end();
      });
      return;
    }
    if (req.method === "GET" && url.pathname === "/auth/me") {
      res.writeHead(200, { "Content-Type": "application/json" });
      return res.end(JSON.stringify({ isAuthenticated: true, accountRole: "owner" }));
    }
    if (req.method === "GET" && url.pathname === "/keep/requests") {
      res.writeHead(200, { "Content-Type": "application/json" });
      return res.end(JSON.stringify({ requests: [] }));
    }
    res.writeHead(404);
    res.end();
  });

  return new Promise((resolve) => {
    server.listen(0, () => resolve({ server, hits, port: server.address().port }));
  });
}

async function runScript(baseUrl, extraEnv = {}, args = []) {
  try {
    const { stdout } = await execFileAsync(
      process.execPath,
      [scriptPath, ...args],
      {
        env: {
          ...process.env,
          SMOKE_API_BASE_URL: baseUrl,
          SMOKE_ACCOUNT_EMAIL: "smoke@example.com",
          ...extraEnv,
        },
      },
    );
    return { stdout, exitCode: 0 };
  } catch (err) {
    // execFile rejects on non-zero exit; stdout/exitCode still useful.
    return { stdout: err.stdout ?? "", exitCode: err.code ?? 1 };
  }
}

test("exchange-code mode does not trigger /auth/signin before exchanging", async () => {
  const { server, hits, port } = await startMockServer();
  try {
    const { stdout, exitCode } = await runScript(`http://localhost:${port}`, {}, [
      "--exchange-code=goodcode",
    ]);

    assert.equal(hits.signin, 0, "auth/signin must not be called in exchange-code mode");
    assert.equal(hits.exchange, 1, "auth/exchange should be called exactly once");
    assert.match(stdout, /auth\/signin.*skip/i);
    assert.equal(exitCode, 0);
  } finally {
    server.close();
  }
});

test("routine mode (no exchange code) still triggers /auth/signin", async () => {
  const { server, hits, port } = await startMockServer();
  try {
    const { exitCode } = await runScript(`http://localhost:${port}`, {
      SMOKE_SESSION_COOKIE: "stored-cookie",
    });

    assert.equal(hits.signin, 1, "auth/signin should be called exactly once in routine mode");
    assert.equal(hits.exchange, 0, "auth/exchange should not be called in routine mode");
    assert.equal(exitCode, 0);
  } finally {
    server.close();
  }
});

test("exchange-code mode fails cleanly on an invalid/expired code without touching signin", async () => {
  const { server, hits, port } = await startMockServer();
  try {
    const { exitCode } = await runScript(`http://localhost:${port}`, {}, [
      "--exchange-code=badcode",
    ]);

    assert.equal(hits.signin, 0);
    assert.equal(hits.exchange, 1);
    assert.equal(exitCode, 1);
  } finally {
    server.close();
  }
});
