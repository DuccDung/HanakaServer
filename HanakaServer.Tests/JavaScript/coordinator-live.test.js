"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { openEdge, delay } = require("./helpers/edge-browser");
const { PublicRealtimeClient, buildPublicRealtimeUrl } = require("../../../hanaka-sport/src/services/publicRealtimeCore");
const { patchMatchObject } = require("../../../hanaka-sport/src/services/realtimeMatchState");
const { matchStatus: getMatchStatus } = require("../../../hanaka-sport/src/services/matchCoordinationState");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);

test("live Razor portal, SQL, referee HTTP and app realtime agree, including a lost committed response", {
    skip: process.env.HANAKA_COORDINATION_E2E !== "1" || !browser, timeout: 120000
}, async () => {
    const artifacts = path.resolve(__dirname, "../../artifacts/coordination-stability");
    const host = JSON.parse(fs.readFileSync(path.join(artifacts, "active-host.json"), "utf8"));
    assert.equal(new URL(host.url).hostname, "127.0.0.1", "Live test may only target the isolated loopback test host");
    assert.equal(host.password, "Coordinator-stability-test-only!");
    const fixture = host.tournaments[0];
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-live-coordinator-"));
    const wait = async (predicate, description) => { for (let i = 0; i < 200; i++) { if (await predicate()) return; await delay(50); } throw Error("Timeout: " + description); };
    const schedule = async () => {
        const response = await fetch(`${host.url}/api/tournaments/${fixture.id}/rounds-with-matches`); assert.ok(response.ok);
        const body = await response.json(); return body.rounds.flatMap(r => r.groups.flatMap(g => g.matches));
    };
    // The first 20 matches belong to soak writers. Choose an unused match assigned
    // to referee 0 so this test can be repeated while the same host is running.
    const rows = await schedule();
    const matchId = fixture.matches.find((id, index) => index >= 20 && index % 10 === 0
        && rows.some(m => m.matchId === id && m.matchStatus === 'NOT_STARTED'));
    assert.ok(matchId, 'No unused match assigned to referee 0 remains in this fixture');
    let mobileMatch = rows.find(m => m.matchId === matchId), received = 0;
    const mobile = new PublicRealtimeClient({ url: buildPublicRealtimeUrl(host.url), WebSocketImpl: WebSocket, reconnectBaseMs: 100 });
    mobile.addListener(message => {
        if (message.payload?.matchId === matchId && message.payload.stateVersion) {
            mobileMatch = patchMatchObject(mobileMatch, message.payload); received++;
        }
    });
    mobile.subscribeTournament(fixture.id);
    let page;
    try {
        await wait(() => mobile.socket?.readyState === WebSocket.OPEN, "app websocket subscription");
        page = await openEdge(browser, directory);
        await page.command("Page.navigate", { url: host.url + "/CoordinatorPortal/Login" });
        const visible = async selector => page.evaluate(`!!document.querySelector(${JSON.stringify(selector)})`).catch(() => false);
        await wait(() => visible("#coordinator-login"), "real login page");
        await page.evaluate(`document.getElementById('account').value=${JSON.stringify(host.coordinator)};document.getElementById('password').value=${JSON.stringify(host.password)};document.querySelector('#coordinator-login button').click()`);
        await wait(() => visible(".match-card"), "real schedule after login");
        await page.evaluate(`document.getElementById('tournament').value=${JSON.stringify(String(fixture.id))};document.getElementById('tournament').dispatchEvent(new Event('change'))`);
        const card = `[data-match-id="${matchId}"]`;
        await wait(() => visible(card + " button"), "assigned tournament match");
        await page.evaluate(`document.getElementById('search').value=${JSON.stringify(String(matchId))};document.getElementById('search').dispatchEvent(new Event('input'));document.querySelector(${JSON.stringify(card + " button")}).click();document.getElementById('edit-court').value='Sân E2E';document.getElementById('edit-preparing').checked=true;document.getElementById('save-coordination').click()`);
        await wait(() => mobileMatch.matchStatus === "PREPARING" && mobileMatch.courtText === "Sân E2E", "app receives real preparation event");
        await wait(() => page.evaluate("!document.getElementById('coordination-dialog').open"), "save complete");
        assert.equal(getMatchStatus(mobileMatch), "PREPARING");
        // Fault injection affects the response transport only; the real API commits to SQL and broadcasts normally.
        await page.evaluate(`(() => { const original = window.fetch; let drop = true; window.fetch = async (...args) => {
            const response = await original(...args); if(drop && args[1]?.method === 'PUT') { drop=false; await response.text(); throw new TypeError('Mất phản hồi sau khi server lưu'); } return response;
        }; document.querySelector(${JSON.stringify(card + " button")}).click();document.getElementById('edit-court').value='Sân đã lưu nhưng mất phản hồi';document.getElementById('save-coordination').click(); })()`);
        await wait(() => mobileMatch.courtText === "Sân đã lưu nhưng mất phản hồi", "committed write reached app despite dropped response");
        await wait(() => page.evaluate("document.getElementById('edit-error').textContent.includes('Chưa xác nhận') && !document.getElementById('reload-editor').disabled"), "uncertain save guarded");
        assert.equal(await page.evaluate("document.getElementById('save-coordination').disabled"), true);
        await page.evaluate("document.getElementById('reload-editor').click()");
        await wait(() => page.evaluate("!document.getElementById('save-coordination').disabled"), "explicit authoritative reload");
        assert.equal(await page.evaluate("document.getElementById('edit-court').value"), "Sân đã lưu nhưng mất phản hồi");
        const login = await fetch(host.url + "/api/referee-auth/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: host.referee, password: host.password }) });
        assert.ok(login.ok);
        const cookie = login.headers.getSetCookie().map(v => v.split(';')[0]).join('; ');
        async function score(a, b, completed) {
            const response = await fetch(`${host.url}/api/referee/matches/${matchId}/score`, { method: "PUT",
                headers: { "Content-Type": "application/json", Cookie: cookie }, body: JSON.stringify({ scoreTeam1: a, scoreTeam2: b, isCompleted: completed }) });
            assert.ok(response.ok, await response.text());
        }
        await score(0, 0, false);
        await wait(() => mobileMatch.matchStatus === "IN_PROGRESS", "zero score starts match on app");
        await wait(() => page.evaluate("document.getElementById('edit-warning').textContent.includes('Trận đã bắt đầu')"), "portal locks on referee start");
        assert.equal(await page.evaluate("document.getElementById('edit-court').disabled && document.getElementById('save-coordination').disabled"), true);
        await page.evaluate("document.getElementById('close-editor').click()");
        await score(11, 8, true);
        await wait(() => mobileMatch.matchStatus === "COMPLETED", "app completion");
        await wait(() => visible(card + ".COMPLETED"), "portal green completed card");
        assert.equal(mobileMatch.scoreTeam1, 11); assert.equal(getMatchStatus(mobileMatch), "COMPLETED");
        const persisted = (await schedule()).find(m => m.matchId === matchId);
        assert.equal(persisted.stateVersion, mobileMatch.stateVersion); assert.equal(persisted.courtText, mobileMatch.courtText);
        assert.ok(received >= 4);
        for (const width of [320, 390, 768, 1280]) {
            await page.command("Emulation.setDeviceMetricsOverride", { width, height: 844, deviceScaleFactor: 1, mobile: false });
            assert.equal(await page.evaluate("document.documentElement.scrollWidth <= innerWidth"), true);
            if(width === 390) {
                const shot = await page.command("Page.captureScreenshot", { format: "png" });
                fs.writeFileSync(path.join(artifacts, "live-portal-390.png"), Buffer.from(shot.data, "base64"));
            }
        }
        fs.writeFileSync(path.join(artifacts, "live-e2e.json"), JSON.stringify({ passed: true, host: host.url,
            tournamentId: fixture.id, matchId, appEvents: received, finalVersion: persisted.stateVersion,
            physicalDevice: false, testedAt: new Date().toISOString() }, null, 2));
    } finally {
        mobile.destroy(); if (page) await page.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith("hanaka-live-coordinator-"));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
});
