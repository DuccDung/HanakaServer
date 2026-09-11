"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { pathToFileURL } = require("node:url");
const test = require("node:test");

const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(p => fs.existsSync(p));

test("referee scoring keeps the form stable through saves, realtime echoes and delayed responses", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../..");
    const view = fs.readFileSync(path.join(root, "HanakaServer/Views/RefereePortal/Matches.cshtml"), "utf8");
    const css = view.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll("@@", "@");
    const markup = view.slice(view.indexOf("<body>") + 6, view.indexOf("<script src="));
    const script = view.match(/<script>\s*([\s\S]*?)<\/script>/)[1];
    const loader = fs.readFileSync(path.join(root, "HanakaServer/wwwroot/js/api-loading.js"), "utf8");
    const loaderCss = fs.readFileSync(path.join(root, "HanakaServer/wwwroot/css/api-loading.css"), "utf8");

    const scenario = async (script, loader) => {
        const output = document.getElementById("result");
        const failures = [];
        let checks = 0;
        const check = (ok, message) => { checks++; if (!ok) failures.push(message); };
        const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
        const wait = async (condition, message) => {
            for (let i = 0; i < 200; i++) { if (condition()) return; await pause(10); }
            throw Error("Timed out: " + message);
        };
        const copy = value => JSON.parse(JSON.stringify(value));
        const byId = id => document.getElementById(id);
        const plus = () => document.querySelector('[data-score-target="inlineScore1"][data-score-step="1"]');
        const emit = (type, payload = { tournamentId: 37 }) => listeners.forEach(fn => fn({ type, payload }));
        const listeners = [];
        const writes = [];
        const reads = [];
        let holdReads = false;
        let listReads = 0;
        let historyId = 0;
        let server = {
            matchId: 764, tournamentId: 37, tournamentTitle: "hanaka", roundKey: "R1", roundLabel: "Vòng 1",
            team1RegistrationId: 10, team2RegistrationId: 20, team1Text: "Đội tiếp sức 02", team2Text: "Đội tiếp sức 06",
            startAt: "2026-09-10T08:39:00", scoreTeam1: 2, scoreTeam2: 6, isCompleted: false,
            canEditScore: true, scoreHistories: [], winnerRegistrationId: null, winnerTeam: null
        };
        const response = data => ({ data: copy(data), headers: { "content-type": "application/json" } });
        const requestHandlers = [], responseHandlers = [];
        window.axios = {
            interceptors: {
                request: { use: fn => requestHandlers.push(fn) },
                response: { use: (success, failure) => responseHandlers.push({ success, failure }) }
            },
            async request(config) {
                for (const fn of requestHandlers) config = await fn(config);
                try {
                    let res;
                    if (config.method === "put") {
                        res = await new Promise((resolve, reject) => writes.push({ config, resolve, reject }));
                    } else if (config.url.endsWith("/me")) {
                        res = response({ fullName: "Trọng tài kiểm thử" });
                    } else {
                        listReads++;
                        const snapshot = response({ items: [server] });
                        res = holdReads ? await new Promise(resolve => reads.push({ resolve, snapshot })) : snapshot;
                    }
                    res.config = config;
                    for (const fn of responseHandlers) res = await fn.success(res);
                    return res;
                } catch (error) {
                    error.config = config;
                    for (const fn of responseHandlers) {
                        try { await fn.failure(error); } catch (next) { error = next; }
                    }
                    throw error;
                }
            },
            get(url, options) { return this.request({ ...options, url, method: "get" }); },
            put(url, data, options) { return this.request({ ...options, url, data, method: "put" }); }
        };
        window.HanakaPublicRealtime = {
            on(fn) { listeners.push(fn); return () => {}; }, subscribeTournament() {}, unsubscribeTournament() {}
        };
        // file:// fixtures cannot navigate to MVC paths. Keep the real click handlers running.
        history.pushState = history.replaceState = () => {};
        const commit = request => {
            const history = { scoreHistoryId: ++historyId, ...request.config.data, createdAt: "2026-09-10T08:40:00", refereeName: "Trọng tài" };
            server = { ...server, ...request.config.data, scoreHistories: [history, ...server.scoreHistories] };
            if (server.isCompleted) {
                server.winnerTeam = server.scoreTeam1 > server.scoreTeam2 ? "1" : "2";
                server.winnerRegistrationId = server.winnerTeam === "1" ? server.team1RegistrationId : server.team2RegistrationId;
            } else {
                server.winnerTeam = null;
                server.winnerRegistrationId = null;
            }
            return response({ ...server, history });
        };
        try {
            (0, eval)(loader);
            (0, eval)(script);
            document.dispatchEvent(new Event("DOMContentLoaded"));
            await wait(() => document.querySelector(".match-card-action"), "initial match list");
            window.openMatchDetail(764);
            await pause(30);
            const originalForm = byId("inlineScoreForm");
            const originalInput = byId("inlineScore1");
            const scroll = document.querySelector(".overlay-body");
            scroll.scrollTop = 140;
            const initialScroll = scroll.scrollTop;
            const buttonOpacity = getComputedStyle(plus()).opacity;
            const buttonRect = plus().getBoundingClientRect();
            let inputFocuses = 0;
            originalInput.addEventListener("focus", () => inputFocuses++);
            plus().click();
            await wait(() => writes.length === 1, "first score request");
            check(originalInput.value === "3", "A point appears immediately");
            check(inputFocuses === 0, "Plus does not focus the numeric input or summon the keyboard");
            check(plus().disabled, "Only one score request may be in flight");
            await pause(250);
            check(!document.querySelector(".hanaka-api-section-overlay:not([hidden]), .hanaka-api-overlay:not([hidden])"), "A slow point save does not cover the scoreboard with a loading overlay");
            check(getComputedStyle(plus()).opacity === buttonOpacity, "Saving does not flash the score buttons between opaque and faded");
            check(plus().getBoundingClientRect().height === buttonRect.height, "Saving preserves the point button dimensions");
            const first = writes.shift();
            const firstResult = commit(first);
            emit("tournament.match.score.updated", server); // Server broadcasts before sending the HTTP response.
            first.resolve(firstResult);
            await pause(650);
            check(byId("inlineScoreForm") === originalForm, "Realtime before the save response keeps the same form");
            check(byId("inlineScore1") === originalInput, "Realtime refresh keeps the score input node");
            check(Math.abs(scroll.scrollTop - initialScroll) < 1, "Save and realtime preserve scroll position");
            check(byId("scoreSaveStatus").textContent.includes("Đã lưu"), "The saved message survives the realtime echo");
            check(byId("inlineScore1").value === "3", "The confirmed point is retained");
            check(!plus().disabled, "Scoring unlocks after the response");
            check(!byId("scoreToast").classList.contains("show"), "A routine point save uses inline status instead of a floating toast");

            const afterFirst = byId("inlineScoreForm");
            emit("tournament.match.score.updated", server); // Echo can also arrive after the HTTP response.
            await pause(500);
            check(byId("inlineScoreForm") === afterFirst, "A late realtime echo also keeps the same form");
            check(byId("inlineScore1").value === "3", "A duplicate event does not add another point");
            check(document.querySelector(".history-section summary small").textContent === "1", "History is refreshed without rebuilding the score form");

            holdReads = true;
            emit("tournament.bracket.updated");
            await wait(() => reads.length > 0, "background list request");
            const staleRead = reads.shift();
            plus().click();
            await wait(() => writes.length === 1, "second score request");
            staleRead.resolve(staleRead.snapshot);
            await pause(30);
            check(byId("inlineScore1").value === "4", "A list response started before the click cannot overwrite the pending point");
            const second = writes.shift();
            second.resolve(commit(second));
            holdReads = false;
            await pause(600);
            check(byId("inlineScore1").value === "4", "Second point remains after deferred synchronization");

            // Manual entry remains available, and realtime must not replace an unsaved draft.
            holdReads = true;
            emit("tournament.bracket.updated");
            await wait(() => reads.length > 0, "background read before manual input");
            const beforeManualRead = reads.shift();
            const manual = byId("inlineScore1");
            manual.value = "9";
            manual.dispatchEvent(new Event("input", { bubbles: true }));
            holdReads = false;
            beforeManualRead.resolve(beforeManualRead.snapshot);
            await pause(30);
            check(byId("inlineScore1") === manual && manual.value === "9", "A delayed list response also preserves manually typed scores");
            emit("tournament.match.score.updated", { ...server, scoreTeam1: 5 });
            await pause(250);
            check(byId("inlineScore1") === manual && manual.value === "9", "Remote scoring preserves a manually entered draft");
            manual.value = String(server.scoreTeam1);
            manual.dispatchEvent(new Event("input", { bubbles: true }));

            plus().click();
            await wait(() => writes.length === 1, "rejected point request");
            writes.shift().reject({ response: { status: 400, data: { message: "Tỷ số không hợp lệ" } } });
            await pause(500);
            check(byId("inlineScore1").value === String(server.scoreTeam1), "A rejected point restores the confirmed score");
            check(!plus().disabled, "A rejected point releases controls");
            check(inputFocuses === 0, "Failure handling does not focus the numeric input either");

            plus().click();
            await wait(() => writes.length === 1, "timed-out point request");
            const timedOut = writes.shift();
            commit(timedOut); // Commit succeeded, but the response was lost.
            timedOut.reject({ code: "ECONNABORTED" });
            await pause(650);
            check(byId("inlineScore1").value === String(server.scoreTeam1), "Timeout recovery uses the server score without retrying or duplicating the point");
            check(!plus().disabled, "Timeout recovery releases controls");
            check(writes.length === 0, "An uncertain write is never automatically replayed");

            byId("inlineIsCompleted").checked = true;
            byId("inlineIsCompleted").dispatchEvent(new Event("change", { bubbles: true }));
            byId("inlineScoreForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
            await wait(() => byId("endMatchConfirm").classList.contains("show"), "completion confirmation");
            check(writes.length === 0, "Completing a match still requires confirmation");
            byId("btnEndMatchCancel").click();
            await pause(30);
            check(writes.length === 0, "Cancelling completion sends no score request");

            const beforeCompleteScroll = scroll.scrollTop;
            const beforeCompleteForm = byId("inlineScoreForm");
            byId("inlineScoreForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
            await wait(() => byId("endMatchConfirm").classList.contains("show"), "second completion confirmation");
            byId("btnEndMatchConfirm").click();
            await wait(() => writes.length === 1, "confirmed completion request");
            const completion = writes.shift();
            check(completion.config.data.isCompleted, "Confirmed completion sends the completion flag");
            const completedResult = commit(completion);
            emit("tournament.match.score.updated", server);
            completion.resolve(completedResult);
            await pause(650);
            check(document.querySelector(".focus-badges").textContent.includes("Đã kết thúc"), "The completed match status still updates");
            check(byId("inlineIsCompleted").checked, "The completion control reflects the saved state");
            check(byId("inlineScore2").closest(".big-score-box").classList.contains("winner"), "The winning team styling updates after completion");
            check(byId("inlineScoreForm") === beforeCompleteForm, "Manual completion and its realtime echo preserve the form");
            check(Math.abs(scroll.scrollTop - beforeCompleteScroll) < 1, "Completing the match preserves scroll while updating its status");

            const badge = () => document.querySelector(".focus-badges").textContent;
            const historyCount = () => Number(document.querySelector(".history-section summary small").textContent);
            const resetMatch = async (score1 = 0, score2 = 0) => {
                byId("btnCloseOverlay").click();
                server = { ...server, scoreTeam1: score1, scoreTeam2: score2, isCompleted: false,
                    winnerTeam: null, winnerRegistrationId: null, scoreHistories: [] };
                const previousReads = listReads;
                emit("tournament.bracket.updated");
                await wait(() => listReads > previousReads, "fresh test match");
                await pause(30);
                window.openMatchDetail(764);
            };
            const markComplete = () => {
                byId("inlineIsCompleted").checked = true;
                byId("inlineIsCompleted").dispatchEvent(new Event("change", { bubbles: true }));
            };
            const step = (team = 1, amount = 1) => document.querySelector(
                `[data-score-target="inlineScore${team}"][data-score-step="${amount}"]`).click();

            for (const c of [
                { name: "First 1-0 without realtime", team: 1 },
                { name: "First 0-1 without realtime", team: 2 },
                { name: "5-2 without realtime", score1: 4, score2: 2, team: 1 },
                { name: "Minus completes 1-0 without realtime", score1: 2, team: 1, step: -1 },
                { name: "First manual 1-0 without realtime", manual: true },
                { name: "Completion event before HTTP response", team: 1, echo: "before" },
                { name: "Completion event after HTTP response", team: 1, echo: "after" }
            ]) {
                await resetMatch(c.score1, c.score2);
                const form = byId("inlineScoreForm"), input = byId("inlineScore1");
                const note = byId("inlineScoreNote");
                note.value = "Tay Hai";
                note.dispatchEvent(new Event("change", { bubbles: true }));
                const details = document.querySelector(".match-info-section");
                details.open = true;
                scroll.scrollTop = 120;
                const beforeScroll = scroll.scrollTop;
                markComplete();
                if (c.manual) {
                    input.value = "1";
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
                } else step(c.team, c.step);
                await wait(() => byId("endMatchConfirm").classList.contains("show"), c.name + " confirmation");
                check(writes.length === 0, c.name + ": confirmation is required before writing");
                byId("btnEndMatchConfirm").click();
                await wait(() => writes.length === 1, c.name + " request");
                // The HTTP request remains pending, independently of realtime availability.
                await pause(500);
                check(!badge().includes("Đã kết thúc") && historyCount() === 0,
                    c.name + ": pending save does not claim a completed result");
                const request = writes.shift(), result = commit(request);
                if (c.echo === "before") emit("tournament.match.score.updated", server);
                request.resolve(result);
                await wait(() => byId("scoreSaveStatus").textContent.includes("Đã lưu"), c.name + " saved");
                check(badge().includes("Đã kết thúc"), c.name + ": HTTP response updates the completed badge immediately");
                check(historyCount() === 1, c.name + ": HTTP response updates history without realtime");
                if (c.echo === "after") emit("tournament.match.score.updated", server);
                await pause(450);
                check(byId("inlineScoreForm") === form && byId("inlineScore1") === input,
                    c.name + ": completion keeps the form and input nodes");
                check(note.isConnected && note.value === "Tay Hai", c.name + ": the selected situation is preserved");
                check(details.isConnected && details.open, c.name + ": expanded details stay open");
                check(Math.abs(scroll.scrollTop - beforeScroll) < 1, c.name + ": scroll stays stable");
                check(byId("inlineIsCompleted").checked, c.name + ": completion switch matches the result");
                check(byId(`inlineScore${server.winnerTeam}`).closest(".big-score-box").classList.contains("winner"),
                    c.name + ": winning team is highlighted");
                check(document.querySelectorAll(".big-score-box.winner").length === 1, c.name + ": only one winner is highlighted");
                check(!document.querySelector(".hanaka-api-section-overlay:not([hidden]), .hanaka-api-overlay:not([hidden])"),
                    c.name + ": saving does not cover the scoreboard");
                if (c.echo) {
                    emit("tournament.match.score.updated", server);
                    await pause(450);
                    check(byId("inlineScoreForm") === form && historyCount() === 1,
                        c.name + ": duplicate event neither rebuilds the form nor duplicates history");
                }
                byId("btnCloseOverlay").click();
                check(document.querySelector(".match-card").classList.contains("completed"),
                    c.name + ": the match list reflects completion when returning");
                window.openMatchDetail(764);
                check(badge().includes("Đã kết thúc") && historyCount() === 1 && writes.length === 0,
                    c.name + ": reopening shows the saved result without another write");
            }

            await resetMatch();
            markComplete();
            step();
            await wait(() => byId("endMatchConfirm").classList.contains("show"), "cancelled step confirmation");
            byId("btnEndMatchCancel").click();
            await pause(30);
            check(writes.length === 0 && byId("inlineScore1").value === "0", "Cancelling completion through plus restores the unsaved point");
            check(!badge().includes("Đã kết thúc") && historyCount() === 0, "Cancelled completion does not update status or history");

            step();
            await wait(() => byId("endMatchConfirm").classList.contains("show"), "rejected completion confirmation");
            byId("btnEndMatchConfirm").click();
            await wait(() => writes.length === 1, "rejected completion request");
            writes.shift().reject({ response: { status: 400, data: { message: "Không thể kết thúc trận thử" } } });
            await wait(() => byId("scoreSaveStatus").classList.contains("error"), "rejected completion feedback");
            await pause(50);
            check(!badge().includes("Đã kết thúc") && historyCount() === 0, "Rejected completion leaves confirmed status and history unchanged");
            check(byId("inlineScore1").value === "0" && !document.querySelector(".big-score-box.winner"), "Rejected completion restores the point without marking a winner");
            check(!plus().disabled && !byId("scoreSaveStatus").textContent.includes("Đã lưu"), "Rejected completion releases controls and does not show success");

            // Realtime changes to the score result can also update an open form in place.
            await resetMatch(1, 0);
            const remoteForm = byId("inlineScoreForm");
            server = { ...server, isCompleted: true, winnerRegistrationId: 10, winnerTeam: "1" };
            emit("tournament.match.score.updated", server);
            await pause(450);
            check(badge().includes("Đã kết thúc") && byId("inlineIsCompleted").checked,
                "Remote completion updates the badge and checkbox");
            check(byId("inlineScoreForm") === remoteForm, "Remote completion preserves an unchanged form");
            server = { ...server, scoreTeam2: 2, winnerRegistrationId: 20, winnerTeam: "2" };
            emit("tournament.match.score.updated", server);
            await pause(450);
            check(byId("inlineScoreForm") === remoteForm && byId("inlineScore2").closest(".big-score-box").classList.contains("winner")
                && !byId("inlineScore1").closest(".big-score-box").classList.contains("winner"), "A changed winner moves the highlight without replacing the form");

            byId("inlineIsCompleted").checked = false;
            byId("inlineIsCompleted").dispatchEvent(new Event("change", { bubbles: true }));
            byId("inlineScoreForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
            await wait(() => writes.length === 1, "reopen match request");
            const reopen = writes.shift();
            reopen.resolve(commit(reopen));
            await wait(() => byId("scoreSaveStatus").textContent.includes("Đã lưu"), "reopened match saved");
            check(badge().includes("Có thể chấm") && !byId("inlineIsCompleted").checked
                && !document.querySelector(".big-score-box.winner"), "Saving an unfinished result clears completed status and winner styling");
            check(byId("inlineScoreForm") === remoteForm, "Reopening a match also preserves the form");

            server = { ...server, canEditScore: false };
            emit("tournament.bracket.updated");
            await wait(() => document.querySelector(".focus-scoreboard"), "read-only match after permission change");
            check(!byId("inlineScoreForm"), "A permission change still rebuilds the view to remove scoring controls");
            const scoreboard = document.querySelector(".focus-scoreboard");
            server = { ...server, isCompleted: true, winnerRegistrationId: 20, winnerTeam: "2" };
            emit("tournament.match.score.updated", server);
            await pause(450);
            check(document.querySelector(".focus-scoreboard") === scoreboard && badge().includes("Đã kết thúc")
                && document.querySelector("[data-live-score='team2']").closest(".focus-team").classList.contains("winner"),
                "Read-only scoring also updates completion and winner without replacing the scoreboard");
            output.textContent = failures.length ? `FAIL: ${failures.join(" | ")}` : `PASS: ${checks} scoring interaction assertions`;
        } catch (error) { output.textContent = `FAIL: ${error.stack}; ${failures.join(" | ")}`; }
    };

    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-referee-scoring-"));
    try {
        const file = path.join(temp, "test.html");
        fs.writeFileSync(file, `<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}\n${loaderCss}\n.d-none{display:none!important}body{line-height:1.5}.fas,.far{display:inline-block;width:1em}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden}</style>${markup}<pre id="result" style="position:fixed;bottom:0;pointer-events:none">RUNNING</pre><script>window.addEventListener('load',()=>(${scenario.toString()})(${JSON.stringify(script)},${JSON.stringify(loader)}));</script>`);
        const result = await new Promise((resolve, reject) => {
            const child = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                `--user-data-dir=${path.join(temp, "profile")}`, "--window-size=430,900", "--virtual-time-budget=30000", "--dump-dom", pathToFileURL(file).href], { windowsHide: true, timeout: 45000 });
            let stdout = "";
            child.stdout.on("data", data => stdout += data);
            child.stderr.resume();
            child.on("error", reject);
            child.on("close", code => resolve({ code, stdout }));
        });
        const status = result.stdout.match(/<pre id="result"[^>]*>([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(result.code, 0);
        assert.match(status || "No browser result", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-referee-scoring-"));
        fs.rmSync(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    }
});
