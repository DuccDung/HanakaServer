"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const test = require("node:test");
const { openEdge } = require("./helpers/edge-browser");
const root = path.resolve(__dirname, "../../HanakaServer");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const source = read("wwwroot/pickleball-web/js/tournament-schedule.js");
const context = { window: {} };
vm.runInNewContext(source, context);
const state = context.window.HanakaTournamentSchedule;
const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("schedule status handles zero-zero start, reopen, BYE and legacy snapshots", () => {
    const fixtures = [
        [{ stateVersion: 1, matchStatus: "NOT_STARTED" }, "NOT_STARTED", true],
        [{ stateVersion: 2, matchStatus: "PREPARING" }, "PREPARING", true],
        [{ stateVersion: 3, matchStatus: "IN_PROGRESS", scoreTeam1: 0, scoreTeam2: 0 }, "IN_PROGRESS", false],
        [{ stateVersion: 5, matchStatus: "IN_PROGRESS", isCompleted: false, winnerRegistrationId: 1 }, "IN_PROGRESS", false],
        [{ stateVersion: 6, isCompleted: true, completionReason: "BYE", scoreTeam1: 0 }, "COMPLETED", false],
        [{ scoreTeam1: 1 }, "IN_PROGRESS", false],
        [{ matchStatus: "NOT_STARTED", scoreTeam1: 5 }, "NOT_STARTED", false]
    ];
    for (const [match, status, editable] of fixtures) {
        assert.equal(state.presentation(match).status, status);
        assert.equal(state.canCoordinate(match), editable);
    }
});

test("schedule rejects older REST/socket versions and clears stale winner and participant metadata", () => {
    const current = { matchId: 1, stateVersion: 8, matchStatus: "COMPLETED", isCompleted: true, winnerRegistrationId: 10,
        winnerTeam: "1", winner: { displayName: "Old winner" }, team1RegistrationId: 10, team1: { displayName: "Team A" } };
    const reopened = state.merge(current, { MatchId: 1, StateVersion: 9, MatchStatus: "IN_PROGRESS", IsCompleted: false, WinnerRegistrationId: null });
    assert.equal(state.presentation(reopened).status, "IN_PROGRESS");
    assert.equal(reopened.winner, null);
    assert.equal(reopened.winnerTeam, null);
    assert.equal(state.merge(reopened, current).stateVersion, 9);
    assert.equal(state.merge(reopened, { scoreTeam1: 99 }).scoreTeam1, undefined);
    const changed = state.merge(reopened, { stateVersion: 10, team1RegistrationId: 20 });
    assert.equal(changed.team1, null);
    assert.equal(state.merge(changed, current).team1, null);
    assert.equal(state.merge({ matchId: 1, stateVersion: 9, team1RegistrationId: 20 }, current).team1, null);
    assert.equal(state.merge({ matchId: 1, stateVersion: 9, team1RegistrationId: null }, current).team1, null);
    assert.equal(state.merge({ matchId: 1, stateVersion: 9, team1RegistrationId: 10 }, current).team1.displayName, "Team A");
});

async function withSchedule(run) {
    const view = read("Views/PickleballWeb/Detail.cshtml");
    const markup = (view.slice(view.indexOf("<div"), view.indexOf("    <nav")) + "</div>")
        .replace("@Model.PageKind", "tournament-schedule-page").replace("@Model.EntityId", "37").replace(/@Model\.\w+/g, "Hanaka");
    const scripts = [read("wwwroot/js/coordinator-http.js"), source, read("wwwroot/pickleball-web/js/pages.js")];
    const css = ["home", "pages", "tournament-schedule"].map(name => read(`wwwroot/pickleball-web/css/${name}.css`)).join("\n");
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-public-coordination-"));
    let session;
    try {
        session = await openEdge(browser, directory);
        await session.command("Page.setDocumentContent", { frameId: (await session.command("Page.getFrameTree")).frameTree.frame.id,
            html: '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><style>' + css + '</style><body>' + markup + '</body></html>' });
        const setup = async scripts => {
            const wait = async (predicate, label = "UI") => { for (let i = 0; i < 300; i++) { if (predicate()) return; await new Promise(r => setTimeout(r, 10)); } throw Error("Timeout: " + label); };
            const check = (condition, label) => { if (!condition) throw Error(label); };
            const pause = () => new Promise(resolve => setTimeout(resolve, 25));
            let listener, mode = "ok", permissionMode = "ok", timeoutMs = 20000, releaseWrite, releaseRead, deferRead = true;
            let userId = 9, allowed = true, readCount = 0, poll;
            const setInterval = window.setInterval.bind(window);
            window.setInterval = (callback, ms) => { if (ms === 30000) poll = callback; return setInterval(callback, ms); };
            const writes = [], subscriptions = [];
            const row = (id, matchStatus = "NOT_STARTED") => ({ matchId: id, tournamentId: 37, stateVersion: 1, matchStatus, isCompleted: matchStatus === "COMPLETED",
                team1RegistrationId: id + 10, team2RegistrationId: id + 20, team1: { displayName: "TEAM VĂN VẢI VÀ NHỮNG NGƯỜI BẠN" },
                team2: { displayName: "TEAM HANAKA" }, scoreTeam1: 0, scoreTeam2: 0, courtText: "Sân 1", addressText: "Nhà Thi Đấu Hanaka", startAt: "2026-09-22T08:00:00" });
            const rows = [row(1), row(2, "PREPARING"), row(3), row(4, "COMPLETED"), row(5)];
            rows[3].winnerRegistrationId = rows[3].team1RegistrationId;
            rows[3].scoreTeam1 = 63; rows[3].scoreTeam2 = 43;
            rows[4].team2RegistrationId = null; rows[4].courtText = null;
            rows[4].team1.displayName = '<img src=x onerror=alert(1)> Đội chờ';
            const rounds = [
                { tournamentRoundMapId: 7, roundLabel: "Vòng 1", groups: [{ tournamentRoundGroupId: 71, groupName: "A", matches: rows }] },
                { tournamentRoundMapId: 8, roundLabel: "Vòng 2", groups: [{ tournamentRoundGroupId: 81, groupName: "B", matches: [row(6)] }] }
            ];
            const response = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
            const abort = options => new Promise((_, reject) => options.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true }));
            window.HanakaPublicRealtime = { on: callback => { listener = callback; return () => listener = null; }, subscribeTournament: id => subscriptions.push(id), unsubscribeTournament: () => {} };
            window.fetch = async (url, options = {}) => {
                if (url.includes("/permissions")) {
                    if (permissionMode === "fail") throw Error("Offline");
                    if (permissionMode === "401") return response({}, 401);
                    return response({ canCoordinate: allowed, userId, requestToken: allowed ? "test-csrf" : null });
                }
                if (options.method === "PUT") {
                    const payload = JSON.parse(options.body);
                    writes.push({ url, body: payload, token: options.headers.RequestVerificationToken });
                    const target = rows.find(row => row.matchId === Number(url.split("/").pop()));
                    if (mode === "401" || mode === "403") return response({ message: "Không còn quyền." }, Number(mode));
                    if (mode === "timeout") return abort(options);
                    if (mode === "conflict") {
                        target.stateVersion++; target.courtText = "Sân người khác";
                        return response({ message: "Trận vừa thay đổi." }, 409);
                    }
                    if (mode === "defer") await new Promise(resolve => releaseWrite = resolve);
                    check(payload.expectedVersion === target.stateVersion, "Wrong expected version");
                    const status = payload.preparing ? "PREPARING" : "NOT_STARTED";
                    if ((target.courtText || "") !== payload.courtText || target.matchStatus !== status) target.stateVersion++;
                    target.courtText = payload.courtText; target.matchStatus = status;
                    if (mode === "lost-after-commit") throw Error("Lost response after commit");
                    if (mode === "bad-json") return new Response("not json", { status: 200 });
                    if (mode === "500") return response({ message: "Server error" }, 500);
                    return response(structuredClone(target));
                }
                readCount++;
                const payload = structuredClone({ tournament: { tournamentId: 37, title: "Hanaka", expectedTeams: 34, matchesCount: rows.length + 1 }, rounds });
                if (deferRead) { deferRead = false; await new Promise(resolve => releaseRead = resolve); }
                return response(payload);
            };
            window.eval(scripts[0]);
            const http = window.HanakaCoordinatorHttp;
            window.HanakaCoordinatorHttp = { request: (path, options) => http.request(path, { ...options, timeoutMs }) };
            window.eval(scripts[1]); window.eval(scripts[2]);
            document.dispatchEvent(new Event("DOMContentLoaded"));
            await wait(() => !!releaseRead);
            // Simulate a referee starting at 0–0 before the initial REST response arrives.
            rows[2].stateVersion = 2; rows[2].matchStatus = "IN_PROGRESS";
            listener({ type: "tournament.match.score.updated", payload: structuredClone(rows[2]) });
            releaseRead(); releaseRead = null;
            await wait(() => document.querySelectorAll("[data-schedule-match-id]").length === 6);
            check(subscriptions.includes(37), "Missing realtime subscription");
            check(!document.querySelector("[data-detail-body] img"), "Team names allowed HTML injection");
            const card = id => document.querySelector(`[data-schedule-match-id="${id}"]`);
            const dialog = document.querySelector(".schedule-coordination"), form = dialog.querySelector("form");
            const field = name => form.elements[name];
            const button = name => form.querySelector(`[data-${name}]`);
            const open = async id => { card(id).querySelector("[data-coordinate-match]").click(); await wait(() => dialog.open, "open editor"); };
            const close = async () => { button("close").click(); await pause(); check(!dialog.open, "Editor did not close"); };
            window.scheduleTest = { wait, check, pause, rows, rounds, writes, card, dialog, form, field, button, open, close,
                mode: value => mode = value, permissionMode: value => permissionMode = value, user: value => userId = value, allowed: value => allowed = value,
                timeout: value => timeoutMs = value, deferRead: () => { releaseRead = null; deferRead = true; }, hasRead: () => !!releaseRead,
                hasWrite: () => !!releaseWrite, releaseRead: () => releaseRead(), releaseWrite: () => releaseWrite(), reads: () => readCount,
                refresh: () => window.dispatchEvent(new Event("focus")), submit: () => form.dispatchEvent(new Event("submit", { cancelable: true })),
                poll: () => { check(!!poll, "Missing 30-second permission/data refresh"); poll(); },
                emit: (payload, type = "tournament.match.score.updated") => listener({ type, payload }),
                reconnect: () => listener({ type: "__public_socket_open__", reconnected: true }) };
            return true;
        };
        assert.equal(await session.evaluate(`(${setup.toString()})(${JSON.stringify(scripts)})`), true);
        await run(session);
    } finally {
        if (session) await session.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith("hanaka-public-coordination-"));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
}

test("public schedule renders four colors, reconciles realtime and preserves tabs, groups and drafts", { skip: !browser, timeout: 60000 }, async () => withSchedule(async session => {
    const artifacts = path.resolve(__dirname, "../../artifacts/public-match-coordination");
    fs.mkdirSync(artifacts, { recursive: true });
    for (const width of [320, 390, 768, 1280]) {
        await session.command("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: false });
        assert.equal(await session.evaluate("document.documentElement.scrollWidth <= innerWidth"), true, `Overflow at ${width}px`);
        if (width === 390 || width === 1280) {
            const shot = await session.command("Page.captureScreenshot", { format: "png", captureBeyondViewport: true });
            fs.writeFileSync(path.join(artifacts, `schedule-${width}.png`), Buffer.from(shot.data, "base64"));
        }
        await session.evaluate("scheduleTest.open(1)");
        assert.equal(await session.evaluate("(() => { const d = scheduleTest.dialog; return d.scrollWidth <= d.clientWidth && d.getBoundingClientRect().right <= innerWidth; })()"), true, `Dialog overflow at ${width}px`);
        if (width === 390) {
            const shot = await session.command("Page.captureScreenshot", { format: "png" });
            fs.writeFileSync(path.join(artifacts, "editor-390.png"), Buffer.from(shot.data, "base64"));
        }
        await session.evaluate("scheduleTest.close()");
    }
    await session.command("Emulation.setDeviceMetricsOverride", { width: 390, height: 400, deviceScaleFactor: 1, mobile: false });
    await session.evaluate("scheduleTest.open(1)");
    assert.equal(await session.evaluate("scheduleTest.dialog.getBoundingClientRect().height <= innerHeight && scheduleTest.dialog.scrollHeight > 0"), true, "Dialog exceeds keyboard-sized viewport");
    await session.evaluate("scheduleTest.close()");
    await session.command("Emulation.setDeviceMetricsOverride", { width: 390, height: 900, deviceScaleFactor: 1, mobile: false });
    const scenario = async () => {
        const t = window.scheduleTest, { check, wait, card } = t;
        for (const [id, status, color] of [[1, "NOT_STARTED", "rgb(255, 255, 255)"], [2, "PREPARING", "rgb(255, 251, 235)"],
            [3, "IN_PROGRESS", "rgb(239, 246, 255)"], [4, "COMPLETED", "rgb(240, 253, 244)"]]) {
            check(card(id).dataset.matchStatus === status, "Wrong match status");
            check(getComputedStyle(card(id)).backgroundColor === color, "Wrong match color");
            check(card(id).querySelector("[data-schedule-status]").textContent.length > 0, "Missing status label");
        }
        check(card(1).querySelector("[data-schedule-court]").textContent.includes("Sân 1"), "Address hid court");
        check(card(1).querySelector("[data-schedule-address]").textContent === "Nhà Thi Đấu Hanaka", "Missing venue");
        check(card(3).querySelector("[data-coordinate-match]").hidden && card(4).querySelector("[data-coordinate-match]").hidden, "Started/completed editable");
        check(card(4).querySelectorAll(".is-winner").length === 2, "Completed winner not highlighted");
        t.rows[3].stateVersion++; t.rows[3].isCompleted = false; t.rows[3].matchStatus = "IN_PROGRESS";
        t.rows[3].winnerRegistrationId = null; t.emit(structuredClone(t.rows[3]));
        check(card(4).dataset.matchStatus === "IN_PROGRESS" && !card(4).querySelector(".is-winner"), "Reopen kept old green/winner");
        t.rows[3].stateVersion++; t.rows[3].isCompleted = true; t.rows[3].matchStatus = "COMPLETED"; t.rows[3].completionReason = "BYE";
        t.emit(structuredClone(t.rows[3]));
        check(card(4).dataset.matchStatus === "COMPLETED", "BYE is not completed");
        const stableCard = card(1);
        await t.open(1); t.field("court").value = "Sân bản nháp";
        t.deferRead(); t.refresh(); await wait(t.hasRead);
        t.rows[0].stateVersion++; t.rows[0].courtText = "Sân mới"; t.rows[0].matchStatus = "PREPARING";
        t.emit(structuredClone(t.rows[0]), "tournament.match.coordination.updated");
        t.rows[1].stateVersion++; t.rows[1].matchStatus = "IN_PROGRESS"; t.emit(structuredClone(t.rows[1]));
        check(card(1).dataset.matchStatus === "PREPARING" && card(2).dataset.matchStatus === "IN_PROGRESS", "Burst dropped one match event");
        check(t.field("court").value === "Sân bản nháp" && t.button("save").disabled, "Realtime lost draft or allowed stale save");
        t.releaseRead(); await t.pause();
        check(card(1) === stableCard && card(1).querySelector("[data-schedule-court]").textContent.includes("Sân mới"), "Late REST replaced card/state");
        t.emit({ ...t.rows[0], stateVersion: 1, courtText: "Sân cũ", matchStatus: "NOT_STARTED" });
        check(card(1).dataset.matchStatus === "PREPARING", "Stale socket overwrote latest state");
        await t.close();
        document.querySelector('[data-tournament-tab-target="8"]').click();
        document.querySelector('[data-tournament-group-toggle="8-81"]').click();
        t.rounds[0].roundLabel = "Vòng mới";
        const reads = t.reads(); t.emit({ tournamentId: 37 }, "tournament.bracket.updated");
        await wait(() => t.reads() > reads && document.querySelector('[data-tournament-tab-target="7"]').textContent === "Vòng mới");
        check(document.querySelector('[data-tournament-tab-target="8"]').classList.contains("is-active"), "Bracket refresh reset tab");
        check(!document.querySelector('[data-tournament-group="8-81"]').classList.contains("is-open"), "Bracket refresh expanded group");
        document.querySelector('[data-tournament-tab-target="7"]').click();
        await t.open(1); t.field("court").value = "Đang nhập";
        t.rows[0].stateVersion++; t.rows[0].matchStatus = "IN_PROGRESS"; t.emit(structuredClone(t.rows[0]));
        check(t.button("save").disabled && t.field("court").value === "Đang nhập", "Referee start did not lock/preserve draft");
        await t.close();
        await t.open(5); t.allowed(false); t.poll(); await wait(() => t.button("save").disabled);
        await wait(() => card(5).querySelector("[data-coordinate-match]").hidden);
        check(t.field("court").disabled, "Revoked operator can edit");
        t.allowed(true); t.user(10); t.refresh(); await wait(() => !t.dialog.open);
        check(!t.dialog.open, "Account switch retained another user's draft");
        return true;
    };
    assert.equal(await session.evaluate(`(${scenario.toString()})()`), true);
}));

test("public coordination handles duplicate saves, conflicts, uncertain commits, session loss and removal", { skip: !browser, timeout: 60000 }, async () => withSchedule(async session => {
    const scenario = async () => {
        const t = window.scheduleTest, { check, wait } = t;
        await t.open(5); t.field("preparing").checked = true; t.submit();
        check(t.writes.length === 0, "Allowed preparing without teams/court");
        t.field("court").value = "Sân chờ"; t.submit();
        check(t.writes.length === 0, "Allowed preparing without two teams");
        await t.close();
        await t.open(1); t.field("court").value = " Sân 2 "; t.field("preparing").checked = true;
        t.mode("defer"); t.submit(); t.submit(); await wait(t.hasWrite);
        check(t.writes.length === 1 && t.button("save").disabled, "Duplicate save escaped guard");
        check(t.writes[0].token === "test-csrf" && t.writes[0].body.courtText === "Sân 2", "Missing CSRF or court trim");
        t.releaseWrite(); await wait(() => !t.dialog.open); await t.pause();
        check(t.card(1).dataset.matchStatus === "PREPARING", "Save without socket did not update UI");
        const unchangedVersion = t.rows[0].stateVersion;
        await t.open(1); t.mode("ok"); t.submit(); await wait(() => !t.dialog.open); await t.pause();
        check(t.rows[0].stateVersion === unchangedVersion, "Unchanged save must keep its version and close normally");
        await t.open(1); t.mode("conflict"); t.field("court").value = "Sân nháp 409"; t.submit();
        await wait(() => !t.button("reload").disabled && t.button("save").disabled);
        check(t.field("court").value === "Sân nháp 409", "Conflict discarded draft");
        check(t.card(1).querySelector("[data-schedule-court]").textContent.includes("Sân người khác"), "Conflict did not reconcile server");
        t.button("reload").click(); await wait(() => !t.button("save").disabled);
        check(t.field("court").value === "Sân người khác", "Explicit reload did not show current data");
        for (const mode of ["lost-after-commit", "bad-json", "500", "timeout"]) {
            t.mode(mode); t.timeout(mode === "timeout" ? 60 : 20000);
            t.field("court").value = "Sân " + mode;
            const before = t.writes.length; t.submit(); await wait(() => t.writes.length === before + 1);
            await wait(() => !t.button("reload").disabled && t.button("save").disabled);
            check(t.field("court").value === "Sân " + mode, "Uncertain write discarded draft");
            t.submit(); await t.pause(); check(t.writes.length === before + 1, "Replayed an uncertain write");
            t.timeout(20000); t.mode("ok"); t.button("reload").click(); await wait(() => !t.button("save").disabled);
            if (mode !== "timeout") check(t.field("court").value === "Sân " + mode, "Did not recover committed write");
        }
        t.mode("ok"); t.field("preparing").checked = false; t.submit(); await wait(() => !t.dialog.open); await t.pause();
        check(t.card(1).dataset.matchStatus === "NOT_STARTED", "Cancel preparation did not reset white");
        await t.open(1); t.mode("403"); t.submit(); await wait(() => t.card(1).querySelector("[data-coordinate-match]").hidden);
        check(t.button("save").disabled, "403 did not lock form");
        await t.close(); t.mode("ok"); t.refresh(); await wait(() => !t.card(1).querySelector("[data-coordinate-match]").hidden);
        await t.open(1); t.permissionMode("fail"); t.refresh(); await wait(() => t.card(1).querySelector("[data-coordinate-match]").hidden);
        check(t.button("save").disabled, "Offline permission check failed open");
        await t.close(); t.permissionMode("ok"); t.refresh(); await wait(() => !t.card(1).querySelector("[data-coordinate-match]").hidden);
        await t.open(1); t.mode("401"); t.submit(); await wait(() => t.card(1).querySelector("[data-coordinate-match]").hidden);
        check(t.button("save").disabled, "401 did not lock form");
        await t.close(); t.mode("ok"); t.refresh(); await wait(() => !t.card(1).querySelector("[data-coordinate-match]").hidden);
        await t.open(1); const removed = t.rows.shift(); t.reconnect(); await wait(() => !t.card(1));
        check(t.button("save").disabled, "Removed match still editable");
        t.emit({ ...removed, stateVersion: 999 }); await t.pause(); check(!t.card(1), "Late event resurrected removed match");
        await t.close(); t.permissionMode("401"); t.refresh(); await wait(() => t.card(5).querySelector("[data-coordinate-match]").hidden);
        check(!!t.card(3), "Anonymous viewer lost public schedule");
        return true;
    };
    assert.equal(await session.evaluate(`(${scenario.toString()})()`), true);
}));
