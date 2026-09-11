"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const scripts = path.resolve(__dirname, "../../HanakaServer/wwwroot/pickleball-web/js");
const sessionScript = fs.readFileSync(path.join(scripts, "web-session.js"), "utf8");
const signedIn = { isAuthenticated: true, user: { userId: 7, fullName: "Session test" } };
const response = (body, status = 200) => new Response(JSON.stringify(body), { status });

function runtime(fetch) {
    const window = { fetch };
    const context = vm.createContext({ window, Response, Error, DOMException });
    vm.runInContext(sessionScript, context);
    return { window, context, session: window.HanakaWebSession };
}

test("session reads only confirm authentication from a valid server response", async () => {
    const replies = [response(signedIn), response({ isAuthenticated: false }), response({}, 401)];
    const { session } = runtime(async () => replies.shift());
    assert.equal((await session.read()).user.userId, 7);
    assert.equal((await session.read()).isAuthenticated, false);
    assert.equal((await session.read()).isAuthenticated, false);
});

test("temporary, network, and malformed responses are unknown rather than signed out", async () => {
    for (const fetch of [
        async () => response({}, 500),
        async () => response({}, 503),
        async () => response({}, 403),
        async () => { throw new TypeError("Network failed"); },
        async () => new Response("<html>not JSON</html>"),
        async () => response(null),
        async () => response({ isAuthenticated: "false" }),
        async () => response({ isAuthenticated: true })
    ]) {
        const { session } = runtime(fetch);
        await assert.rejects(session.read(), error => error.isSessionUnavailable === true &&
            error.message === session.unavailableMessage && !session.isAborted(error));
    }
});

test("browser aborts and HTTP 499 remain cancellation", async () => {
    const abort = new DOMException("The operation was aborted", "AbortError");
    const native = runtime(async () => { throw abort; }).session;
    await assert.rejects(native.read(), error => error === abort && native.isAborted(error));
    const remote = runtime(async () => response({}, 499)).session;
    await assert.rejects(remote.read(), error => remote.isAborted(error) && !error.isSessionUnavailable);
});

test("read propagates its caller signal and does not share cancellation between requests", async () => {
    const calls = [];
    const { session } = runtime((url, options) => {
        calls.push({ url, options });
        return new Promise((resolve, reject) => {
            options.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
            calls.at(-1).resolve = () => resolve(response(signedIn));
        });
    });
    const first = new AbortController(), second = new AbortController();
    const one = session.read({ signal: first.signal, hanakaLoading: "silent" });
    const two = session.read({ signal: second.signal });
    const aborted = assert.rejects(one, error => session.isAborted(error));
    first.abort();
    calls[1].resolve();
    await aborted;
    assert.equal((await two).user.userId, 7);
    assert.equal(calls[0].url, "/api/web-auth/me");
    assert.equal(calls[0].options.signal, first.signal);
    assert.equal(calls[0].options.cache, "no-store");
    assert.equal(calls[0].options.credentials, "same-origin");
    assert.equal(calls[0].options.hanakaLoading, "silent");
    assert.equal(calls[0].options.method, "GET");
});

test("session results are not cached across logout or a change of account", async () => {
    const replies = [response(signedIn), response({ isAuthenticated: false }),
        response({ isAuthenticated: true, user: { userId: 9 } })];
    const { session } = runtime(async () => replies.shift());
    assert.equal((await session.read()).user.userId, 7);
    assert.equal((await session.read()).isAuthenticated, false);
    assert.equal((await session.read()).user.userId, 9);
});

test("home keeps the last confirmed identity on failure and handles confirmed logout", async () => {
    let reply = () => response(signedIn);
    const { window, context } = runtime(async () => reply());
    const avatar = { classList: { add() {}, remove() {} }, setAttribute() {} };
    const document = {
        querySelector(selector) { return selector.includes("avatar-icon") ? avatar : null; },
        querySelectorAll() { return []; },
        addEventListener() {}
    };
    Object.assign(context, { document, URL });
    window.addEventListener = () => {};
    let home = fs.readFileSync(path.join(scripts, "home.js"), "utf8");
    const end = home.lastIndexOf("})();");
    home = home.slice(0, end) + "window.__home = { state, loadAuthSession, updateAuthEntry };" + home.slice(end);
    vm.runInContext(home, context);

    window.__home.updateAuthEntry();
    assert.equal(avatar.href, "/PickleballWeb/Account", "unknown state must not become a login link");
    reply = () => response({}, 500);
    await window.__home.loadAuthSession();
    assert.equal(window.__home.state.session, null);
    assert.equal(avatar.title, "Tài khoản");
    reply = () => response(signedIn);
    await window.__home.loadAuthSession();
    assert.equal(avatar.title, "Session test");
    reply = () => response({}, 500);
    await window.__home.loadAuthSession();
    assert.equal(window.__home.state.session.user.userId, 7);
    assert.equal(avatar.title, "Session test");
    reply = () => { throw new DOMException("Aborted", "AbortError"); };
    await window.__home.loadAuthSession();
    assert.equal(window.__home.state.session.user.userId, 7);
    reply = () => response({ isAuthenticated: false });
    await window.__home.loadAuthSession();
    assert.equal(window.__home.state.session.isAuthenticated, false);
    assert.equal(avatar.title, "Đăng nhập");
});

test("public registration data stays available when the session cannot be checked", async () => {
    const { window, context } = runtime(async () => response({}, 500));
    context.document = { addEventListener() {}, querySelector() { return null; } };
    window.addEventListener = () => {};
    let source = fs.readFileSync(path.join(scripts, "pages.js"), "utf8");
    const end = source.lastIndexOf("})();");
    source = source.slice(0, end) + `
        fetchJson = async () => ({ tournament: { tournamentId: 37 }, items: [] });
        window.__pages = { loadTournamentRegistrationsPage, resolveTournamentRegistrationInvite, loadSelfRatingPage };
    ` + source.slice(end);
    vm.runInContext(source, context);
    const data = await window.__pages.loadTournamentRegistrationsPage(37, "silent");
    assert.equal(data.detail.tournamentId, 37);
    assert.equal(data.sessionUnavailable, true);
    const action = window.__pages.resolveTournamentRegistrationInvite({
        waitingPair: true, player1: { userId: 8 }, player2: { isWaitingSlot: true }
    }, data);
    assert.equal(action.disabled, true);
    assert.equal(action.mode, "unavailable");
    await assert.rejects(window.__pages.loadSelfRatingPage(null, "silent"), error => error.isSessionUnavailable);
});
