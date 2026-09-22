"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const script = fs.readFileSync(path.resolve(__dirname, "../../HanakaServer/wwwroot/js/coordinator-http.js"), "utf8");
function client(fetch) {
    const window = {};
    vm.runInNewContext(script, { window, fetch, AbortController, setTimeout, clearTimeout });
    return window.HanakaCoordinatorHttp.request;
}

test("stalled request times out, aborts transport and marks a write as unconfirmed", async () => {
    let calls = 0, aborted = false;
    const request = client((_url, { signal }) => new Promise((_resolve, reject) => {
        calls++; signal.addEventListener("abort", () => { aborted = true; reject(new DOMException("aborted", "AbortError")); });
    }));
    await assert.rejects(request("/save", { method: "PUT", timeoutMs: 20 }), error => error.uncertain && error.message.includes("quá lâu"));
    assert.equal(calls, 1); assert.equal(aborted, true);
});

test("timeout covers a stalled response body after HTTP headers arrived", async () => {
    const request = client(async (_url, { signal }) => ({ ok: true, status: 200,
        json: () => new Promise((_resolve, reject) => signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")))) }));
    await assert.rejects(request("/save", { method: "PUT", timeoutMs: 20 }), error => error.uncertain && error.message.includes("quá lâu"));
});

test("lost successful write response is never automatically replayed", async () => {
    let committed = 0;
    const request = client(async () => { committed++; throw new TypeError("Connection lost after commit"); });
    await assert.rejects(request("/save", { method: "PUT" }), error => error.uncertain === true);
    assert.equal(committed, 1);
});

test("malformed JSON and incomplete success cannot clear the current schedule or falsely finish a save", async () => {
    const malformed = client(async () => new Response("<html>proxy error</html>"));
    await assert.rejects(malformed("/save", { method: "PUT" }), error => error.uncertain && error.message.includes("không hợp lệ"));
    const incomplete = client(async () => new Response("{}"));
    await assert.rejects(incomplete("/session", { validate: body => Array.isArray(body.tournaments) }), /chưa đầy đủ/);
    await assert.rejects(incomplete("/save", { method: "PUT", validate: body => body.stateVersion > 0 }), error => error.uncertain === true);
});

test("authentication, validation and concurrency errors retain status and are not uncertain commits", async () => {
    for (const status of [400, 401, 403, 409]) {
        const request = client(async () => new Response(JSON.stringify({ message: "Từ chối" }), { status }));
        await assert.rejects(request("/save", { method: "PUT" }), error => error.status === status && !error.uncertain && error.message === "Từ chối");
    }
});

test("server failures require reconciliation, including a non-JSON proxy response", async () => {
    const request = client(async () => new Response("Gateway failed", { status: 502 }));
    await assert.rejects(request("/save", { method: "PUT" }), error => error.status === 502 && error.uncertain === true);
});

test("switching tournaments cancels the previous read without treating it as a timeout", async () => {
    const controller = new AbortController();
    const request = client((_url, { signal }) => new Promise((_resolve, reject) => signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")))));
    const promise = request("/schedule", { signal: controller.signal }); controller.abort();
    await assert.rejects(promise, error => error.name === "AbortError" && !error.uncertain);
});

test("successful request keeps the caller headers and removes the cancellation listener", async () => {
    const controller = new AbortController(); let received, removed = 0;
    const original = controller.signal.removeEventListener.bind(controller.signal);
    controller.signal.removeEventListener = (...args) => { removed++; return original(...args); };
    const request = client(async (_url, options) => { received = options; return new Response('{"ok":true}'); });
    const result = await request("/read", { signal: controller.signal, headers: { RequestVerificationToken: "csrf-test" } });
    assert.equal(result.ok, true); assert.equal(received.headers.RequestVerificationToken, "csrf-test"); assert.equal(removed, 1);
});
