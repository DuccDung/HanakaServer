"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { openEdge, delay } = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);

test("live portal displays twenty simultaneous updates on a 2000-match schedule within two seconds", {
    skip: process.env.HANAKA_COORDINATION_E2E !== "1" || !browser, timeout: 120000
}, async () => {
    const artifacts = path.resolve(__dirname, "../../artifacts/coordination-stability");
    const host = JSON.parse(fs.readFileSync(path.join(artifacts, "active-host.json"), "utf8"));
    assert.equal(new URL(host.url).hostname, "127.0.0.1"); assert.equal(host.password, "Coordinator-stability-test-only!");
    const fixture = host.tournaments[2];
    const schedule = await fetch(`${host.url}/api/tournaments/${fixture.id}/rounds-with-matches`).then(r => r.json());
    const targets = schedule.rounds.flatMap(r => r.groups.flatMap(g => g.matches)).filter(m => m.matchId >= fixture.matches[30] && m.matchStatus === 'NOT_STARTED').slice(0, 20);
    assert.equal(targets.length, 20);
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-large-coordinator-"));
    let page;
    const wait = async (predicate, label) => { for (let i = 0; i < 200; i++) { if (await predicate()) return; await delay(50); } throw Error('Timeout: ' + label); };
    try {
        page = await openEdge(browser, directory);
        await page.command('Page.navigate', { url: host.url + '/CoordinatorPortal/Login' });
        await wait(() => page.evaluate("!!document.getElementById('account')").catch(() => false), 'login');
        await page.evaluate(`document.getElementById('account').value=${JSON.stringify(host.coordinator)};document.getElementById('password').value=${JSON.stringify(host.password)};document.querySelector('#coordinator-login button').click()`);
        await wait(() => page.evaluate("document.querySelectorAll('.match-card').length === 2000").catch(() => false), '2000 match cards');
        const burst = async targets => {
            const pending = new Set(targets.map(m => m.matchId));
            const latencies = [];
            const started = performance.now();
            const observer = new MutationObserver(() => {
                for (const id of pending) if (document.querySelector(`[data-match-id="${id}"].PREPARING`)) {
                    latencies.push(performance.now() - started); pending.delete(id);
                }
            });
            observer.observe(document.getElementById('matches'), { childList: true, subtree: true, attributes: true });
            try {
                const token = document.querySelector('[name="__RequestVerificationToken"]').value;
                await Promise.all(targets.map(async m => {
                    const response = await fetch(`/api/coordinator-portal/matches/${m.matchId}`, { method: 'PUT',
                        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                        body: JSON.stringify({ expectedVersion: m.stateVersion, courtText: 'Sân kiểm thử tải ' + m.matchId, preparing: true }) });
                    if (!response.ok) throw Error('Write failed: ' + response.status);
                }));
                for (let i = 0; pending.size && i < 100; i++) await new Promise(r => setTimeout(r, 20));
                if (pending.size) throw Error('Missing updates: ' + [...pending]);
                latencies.sort((a, b) => a - b);
                return { count: latencies.length, p50Ms: latencies[9], p95Ms: latencies[18], maxMs: latencies[19], latencies };
            } finally { observer.disconnect(); }
        };
        const result = await page.evaluate(`(${burst.toString()})(${JSON.stringify(targets.map(m => ({ matchId: m.matchId, stateVersion: m.stateVersion })))})`, 60000);
        fs.writeFileSync(path.join(artifacts, "large-schedule-browser.json"), JSON.stringify({ ...result, matchCount: 2000, testedAt: new Date().toISOString() }, null, 2));
        assert.ok(result.p95Ms < 2000, `UI p95 ${Math.round(result.p95Ms)}ms exceeded 2 seconds`);
    } finally {
        if (page) await page.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith('hanaka-large-coordinator-'));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
});
