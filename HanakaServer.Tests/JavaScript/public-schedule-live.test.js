"use strict";
const test = require("node:test");
const { assert, fs, path, artifacts, browser, manifest, wait, delay, openPage, navigate, login, openEditor, saveEditor, cardStatus } = require("./helpers/public-schedule-live");
const { PublicRealtimeClient, buildPublicRealtimeUrl } = require("../../../hanaka-sport/src/services/publicRealtimeCore");
const { patchMatchObject } = require("../../../hanaka-sport/src/services/realtimeMatchState");

test("real public web cookie, portal, referee, SQL and app agree through no-op, lost response, reopen and reconnect", {
    skip: process.env.HANAKA_COORDINATION_E2E !== "1" || !browser, timeout: 120000
}, async () => {
    const host = manifest(), fixture = host.tournaments[0];
    const read = async () => (await (await fetch(`${host.url}/api/tournaments/${fixture.id}/rounds-with-matches`)).json()).rounds.flatMap(r => r.groups.flatMap(g => g.matches));
    const rows = await read();
    const id = fixture.matches.find((id, i) => i >= 20 && i % 10 === 0 && rows.some(row => row.matchId === id && row.matchStatus === "NOT_STARTED"));
    assert.ok(id, "No unused match assigned to referee 0");
    let mobileMatch = rows.find(row => row.matchId === id), subscribed = false, events = 0;
    const mobile = new PublicRealtimeClient({ url: buildPublicRealtimeUrl(host.url), WebSocketImpl: WebSocket, reconnectBaseMs: 100 });
    mobile.addListener(message => {
        if (message.type === "tournament.subscribed") subscribed = true;
        if (message.payload?.matchId === id && message.payload.stateVersion) { mobileMatch = patchMatchObject(mobileMatch, message.payload); events++; }
    });
    mobile.subscribeTournament(fixture.id);
    let page, portal;
    const failures = [];
    try {
        await wait(() => subscribed, "app subscription");
        page = await openPage(); await navigate(page, host, fixture);
        assert.equal(await page.evaluate("document.querySelectorAll('[data-coordinate-match]:not([hidden])').length"), 0);
        await login(page, host); await openEditor(page, id); await saveEditor(page, "Sân web tích hợp");
        await wait(() => page.evaluate("!document.querySelector('.schedule-coordination').open"), "first save");
        await wait(() => mobileMatch.courtText === "Sân web tích hợp", "app preparation event");
        portal = await openPage();
        await portal.command("Page.navigate", { url: host.url + "/CoordinatorPortal/Login" });
        await wait(() => portal.evaluate("!!document.getElementById('account')").catch(() => false), "portal login");
        await portal.evaluate(`document.getElementById('account').value=${JSON.stringify(host.coordinator)}; document.getElementById('password').value=${JSON.stringify(host.password)}; document.querySelector('#coordinator-login button').click()`);
        await wait(() => portal.evaluate("!!document.querySelector('.match-card')").catch(() => false), "portal schedule loaded");
        await portal.evaluate(`document.getElementById('tournament').value='${fixture.id}'; document.getElementById('tournament').dispatchEvent(new Event('change'))`);
        await wait(() => portal.evaluate(`!!document.querySelector('[data-match-id="${id}"].PREPARING')`), "portal sees public preparation");
        const version = mobileMatch.stateVersion;
        await openEditor(page, id); await saveEditor(page, "Sân web tích hợp");
        await wait(() => page.evaluate("!document.querySelector('.schedule-coordination').open"), "unchanged save must succeed");
        assert.equal((await read()).find(row => row.matchId === id).stateVersion, version);
        await openEditor(page, id);
        await page.evaluate(`(() => { const original = window.fetch; let drop = true;
            window.fetch = async (...args) => { const response = await original(...args);
                if (drop && args[1]?.method === 'PUT') { drop = false; await response.text(); throw new TypeError('Lost response after real commit'); }
                return response; }; })()`);
        await saveEditor(page, "Sân đã lưu nhưng mất phản hồi");
        await wait(() => mobileMatch.courtText === "Sân đã lưu nhưng mất phản hồi", "committed update reached app");
        await wait(() => page.evaluate("document.querySelector('[data-save]').disabled && !document.querySelector('[data-reload]').disabled"), "uncertain write locked");
        assert.equal(await page.evaluate("document.getElementById('schedule-court').value"), "Sân đã lưu nhưng mất phản hồi");
        await page.evaluate("document.querySelector('[data-reload]').click()");
        await wait(() => page.evaluate("!document.querySelector('[data-save]').disabled"), "reconcile committed write");
        // Real cookie changes while an existing public form is open must invalidate that draft.
        await login(page, host, 1);
        await wait(() => page.evaluate("!document.querySelector('.schedule-coordination').open"), "account change closes old draft");
        await openEditor(page, id);
        const refereeLogin = await fetch(host.url + "/api/referee-auth/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: host.referee, password: host.password }) });
        assert.ok(refereeLogin.ok);
        const cookie = refereeLogin.headers.getSetCookie().map(value => value.split(";")[0]).join("; ");
        const score = async (a, b, isCompleted) => {
            const response = await fetch(`${host.url}/api/referee/matches/${id}/score`, { method: "PUT", headers: { "Content-Type": "application/json", Cookie: cookie },
                body: JSON.stringify({ scoreTeam1: a, scoreTeam2: b, isCompleted }) });
            assert.ok(response.ok, await response.text());
        };
        await score(0, 0, false);
        await wait(() => cardStatus(page, id, "IN_PROGRESS"), "zero-zero running on web");
        await wait(() => mobileMatch.matchStatus === "IN_PROGRESS", "zero-zero running on app");
        assert.equal(await page.evaluate("document.getElementById('schedule-court').disabled && document.querySelector('[data-save]').disabled"), true);
        await page.evaluate("document.querySelector('[data-close]').click()");
        await score(11, 8, true); await wait(() => cardStatus(page, id, "COMPLETED"), "web completed");
        await wait(() => portal.evaluate(`!!document.querySelector('[data-match-id="${id}"].COMPLETED')`), "portal completed");
        // Close/reopen the actual socket through the same lifecycle used by bfcache.
        await page.evaluate("window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }))");
        await score(0, 0, false);
        await page.evaluate("window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
        await wait(() => cardStatus(page, id, "IN_PROGRESS"), "reconnect/reopen restores latest state");
        assert.equal(await page.evaluate(`document.querySelector('[data-schedule-match-id="${id}"]').querySelectorAll('.is-winner').length`), 0);
        await wait(() => mobileMatch.matchStatus === "IN_PROGRESS", "app reopened");
        const persisted = (await read()).find(row => row.matchId === id);
        assert.equal(persisted.stateVersion, mobileMatch.stateVersion);
        const history = await page.command("Page.getNavigationHistory");
        const scheduleEntry = history.entries[history.currentIndex].id;
        await page.command("Page.navigate", { url: `${host.url}/PickleballWeb/Tournament/${fixture.id}/Bracket` });
        await wait(() => page.evaluate("location.pathname.endsWith('/Bracket')").catch(() => false), "navigate away");
        await page.command("Page.navigateToHistoryEntry", { entryId: scheduleEntry });
        await wait(() => cardStatus(page, id, "IN_PROGRESS").catch(() => false), "Back restores public schedule");
        for (const width of [320, 390, 768, 1280]) {
            await page.command("Emulation.setDeviceMetricsOverride", { width, height: 844, deviceScaleFactor: 1, mobile: false });
            assert.equal(await page.evaluate("document.documentElement.scrollWidth <= innerWidth"), true, `overflow ${width}`);
        }
        const shot = await page.command("Page.captureScreenshot", { format: "png" });
        fs.writeFileSync(path.join(artifacts, "public-live.png"), Buffer.from(shot.data, "base64"));
        fs.writeFileSync(path.join(artifacts, "public-live.json"), JSON.stringify({ passed: true, testedAt: new Date().toISOString(), matchId: id, appEvents: events, finalVersion: persisted.stateVersion, physicalDevice: false }, null, 2));
    } catch (error) { failures.push(error.stack); fs.writeFileSync(path.join(artifacts, "public-live-failure.json"), JSON.stringify({ failures }, null, 2)); throw error; }
    finally { mobile.destroy(); if (page) await page.close(); if (portal) await portal.close(); }
});

test("real public schedule applies twenty simultaneous updates among 2000 matches within two seconds", {
    skip: process.env.HANAKA_COORDINATION_E2E !== "1" || !browser, timeout: 120000
}, async () => {
    const host = manifest(), fixture = host.tournaments[2];
    const page = await openPage();
    try {
        await navigate(page, host, fixture); await login(page, host);
        const run = async fixture => {
            const response = await fetch(`/api/tournaments/${fixture.id}/rounds-with-matches`);
            const rows = (await response.json()).rounds.flatMap(r => r.groups.flatMap(g => g.matches));
            const targets = rows.filter(row => fixture.matches.slice(30, 70).includes(row.matchId) && ['NOT_STARTED', 'PREPARING'].includes(row.matchStatus)).slice(0, 20);
            if (targets.length !== 20) throw Error("Missing test targets");
            const permission = await (await fetch(`/api/coordination/tournaments/${fixture.id}/permissions`)).json();
            const pending = new Set(targets.map(row => row.matchId)), timings = [];
            const marker = "Burst " + Date.now(), started = performance.now();
            const unchanged = document.querySelector(`[data-schedule-match-id="${fixture.matches[75]}"]`);
            const observer = new MutationObserver(() => {
                for (const id of pending) if (document.querySelector(`[data-schedule-match-id="${id}"] [data-schedule-court]`)?.textContent.includes(marker)) {
                    timings.push(performance.now() - started); pending.delete(id);
                }
            });
            observer.observe(document.querySelector('[data-detail-body]'), { childList: true, subtree: true });
            try {
                await Promise.all(targets.map(async row => {
                    const saved = await fetch(`/api/coordination/matches/${row.matchId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json', RequestVerificationToken: permission.requestToken },
                        body: JSON.stringify({ expectedVersion: row.stateVersion, courtText: marker, preparing: true }) });
                    if (!saved.ok) throw Error("Burst write failed " + saved.status);
                }));
                for (let i = 0; pending.size && i < 200; i++) await new Promise(r => setTimeout(r, 25));
                if (pending.size) throw Error("Missed updates: " + [...pending]);
                if (unchanged !== document.querySelector(`[data-schedule-match-id="${fixture.matches[75]}"]`)) throw Error("Unchanged card recreated");
                timings.sort((a, b) => a - b);
                return { matches: rows.length, updates: timings.length, p50Ms: timings[9], p95Ms: timings[18], maxMs: timings[19] };
            } finally { observer.disconnect(); }
        };
        const result = await page.evaluate(`(${run.toString()})(${JSON.stringify(fixture)})`, 60000);
        fs.writeFileSync(path.join(artifacts, "public-burst.json"), JSON.stringify({ ...result, testedAt: new Date().toISOString() }, null, 2));
        assert.ok(result.p95Ms < 2000, `UI p95 ${result.p95Ms}ms exceeds 2s`);
    } finally { await page.close(); }
});
