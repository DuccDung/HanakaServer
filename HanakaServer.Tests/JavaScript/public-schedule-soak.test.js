"use strict";
const test = require("node:test");
const { assert, fs, path, artifacts, browser, manifest, wait, delay, openPage, navigate, login, openEditor, saveEditor } = require("./helpers/public-schedule-live");
const minutes = Math.max(1, Math.min(180, Number(process.env.HANAKA_PUBLIC_BROWSER_SOAK_MINUTES) || 90));

test("public schedule stays responsive with stable DOM, listeners and retained heap during a real 90-minute session", {
    skip: process.env.HANAKA_PUBLIC_BROWSER_SOAK !== "1" || !browser, timeout: (minutes + 4) * 60000
}, async () => {
    const host = manifest(), samples = [], failures = [], baselines = new Map();
    const prefix = path.join(artifacts, "public-browser-soak-" + new Date().toISOString().replace(/[:.]/g, "-"));
    const page = await openPage();
    let started, deadline, phase = -1, turn = 0;
    try {
        await page.command("Performance.enable");
        // Count only actual intervals currently owned by this document; collect browser errors too.
        await page.command("Page.addScriptToEvaluateOnNewDocument", { source: `(() => {
            const intervals = new Map(), originalSet = window.setInterval, originalClear = window.clearInterval, originalFetch = window.fetch;
            window.__stability = { intervals, permissions: 0, schedules: 0, errors: [] };
            window.setInterval = function (...args) { const id = originalSet.apply(this, args); intervals.set(id, args[1]); return id; };
            window.clearInterval = function (id) { intervals.delete(id); return originalClear.call(this, id); };
            window.fetch = function (...args) { const url = String(args[0]);
                if (url.includes('/permissions')) window.__stability.permissions++;
                if (url.includes('/rounds-with-matches')) window.__stability.schedules++;
                return originalFetch.apply(this, args); };
            window.addEventListener('error', event => { if (event.error) window.__stability.errors.push(String(event.error)); });
            window.addEventListener('unhandledrejection', event => window.__stability.errors.push(String(event.reason)));
        })()` });
        await navigate(page, host, host.tournaments[0]); await login(page, host);
        started = Date.now(); deadline = started + minutes * 60000;
        while (Date.now() < deadline) {
            const nextPhase = Math.min(2, Math.floor((Date.now() - started) / (minutes * 60000 / 3)));
            const fixture = host.tournaments[nextPhase];
            if (phase !== nextPhase) { await navigate(page, host, fixture); phase = nextPhase; }
            const id = fixture.matches[90]; // Reserved from both the server load and burst tests.
            await openEditor(page, id);
            await page.evaluate("document.getElementById('schedule-court').value='Bản nhập đang giữ'; window.dispatchEvent(new Event('focus'))");
            await wait(() => page.evaluate("!document.querySelector('[data-save]').disabled"), "focus permission refresh");
            assert.equal(await page.evaluate("document.getElementById('schedule-court').value"), "Bản nhập đang giữ");
            const court = `Soak ${phase}/${turn}`;
            await saveEditor(page, court, turn % 2 === 0);
            await wait(() => page.evaluate("!document.querySelector('.schedule-coordination').open"), "soak save");
            if (turn % 5 === 2) {
                await page.command("Network.emulateNetworkConditions", { offline: true, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
                await page.evaluate("window.dispatchEvent(new Event('focus'))"); await delay(700);
                await page.command("Network.emulateNetworkConditions", { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
                await page.evaluate("window.dispatchEvent(new Event('online'))");
                await wait(() => page.evaluate("document.querySelector('.schedule-notice').hidden"), "network recovery");
            }
            if (turn % 5 === 3) {
                await page.evaluate("window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true })); window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
            }
            if (turn % 10 === 9) {
                const history = await page.command("Page.getNavigationHistory"), entryId = history.entries[history.currentIndex].id;
                // A small page avoids counting a cached 2000-card document as a leak in the 100-card phase.
                await page.command("Page.navigate", { url: host.url + "/PickleballWeb/Rules" });
                await wait(() => page.evaluate("location.pathname.endsWith('/Rules')").catch(() => false), "navigate away");
                await page.command("Page.navigateToHistoryEntry", { entryId });
                await wait(() => page.evaluate(`location.pathname.includes('/${fixture.id}/') && document.querySelectorAll('[data-schedule-match-id]').length === ${fixture.matches.length}`).catch(() => false), "Back navigation");
            }
            // Heartbeat is intentionally absent while the socket is reconnecting. Wait for the new
            // connection, then distinguish its 25s timer from the schedule's independent 30s poll.
            await wait(() => page.evaluate("[...__stability.intervals.values()].filter(ms => ms === 25000).length === 1"), "socket heartbeat restored");
            await page.command("HeapProfiler.collectGarbage");
            const metrics = Object.fromEntries((await page.command("Performance.getMetrics")).metrics.map(metric => [metric.name, metric.value]));
            const ui = await page.evaluate(`(async () => {
                const data = await (await fetch('/api/tournaments/${fixture.id}/rounds-with-matches')).json();
                const match = data.rounds.flatMap(r => r.groups.flatMap(g => g.matches)).find(m => m.matchId === ${id});
                const card = document.querySelector('[data-schedule-match-id="${id}"]');
                return { cards: document.querySelectorAll('[data-schedule-match-id]').length, intervals: __stability.intervals.size,
                    polls: [...__stability.intervals.values()].filter(ms => ms === 30000).length,
                    heartbeats: [...__stability.intervals.values()].filter(ms => ms === 25000).length,
                    permissions: __stability.permissions, schedules: __stability.schedules, errors: [...__stability.errors],
                    court: match.courtText, status: match.matchStatus, displayedStatus: card.dataset.matchStatus,
                    displayedCourt: card.querySelector('[data-schedule-court]').textContent };
            })()`);
            assert.equal(ui.cards, fixture.matches.length); assert.equal(ui.court, court); assert.ok(ui.displayedCourt.includes(court));
            assert.equal(ui.status, ui.displayedStatus); assert.deepEqual(ui.errors, []);
            const sample = { elapsedSeconds: Math.round((Date.now() - started) / 1000), phase, matches: fixture.matches.length,
                heapBytes: metrics.JSHeapUsedSize, domNodes: metrics.Nodes, listeners: metrics.JSEventListeners, ...ui };
            if (!baselines.has(phase)) baselines.set(phase, sample);
            const baseline = baselines.get(phase);
            assert.ok(sample.heapBytes < baseline.heapBytes * 2 + 10 * 1024 * 1024, "Retained heap exceeded phase guard");
            assert.ok(sample.domNodes <= baseline.domNodes * 1.2 + 1000, "Detached DOM accumulated");
            assert.ok(sample.listeners <= baseline.listeners * 1.2 + 100, "Event listeners accumulated");
            assert.equal(sample.polls, 1, "Schedule polling stopped or duplicated");
            assert.equal(sample.heartbeats, 1, "Socket heartbeat stopped or duplicated");
            samples.push(sample); fs.appendFileSync(prefix + ".jsonl", JSON.stringify(sample) + "\n");
            console.log(`Public browser soak ${sample.elapsedSeconds}s/${minutes * 60}s: ${ui.cards} cards, heap ${Math.round(sample.heapBytes / 1024)} KiB, ${sample.listeners} listeners, ${sample.intervals} intervals`);
            // Allow a real polling interval to elapse, without advancing or mocking the browser clock.
            const idleBefore = await page.evaluate("({ schedules: __stability.schedules, permissions: __stability.permissions })");
            const nextSample = Math.min(deadline, Date.now() + 60000);
            while (Date.now() < nextSample) await delay(Math.min(5000, nextSample - Date.now()));
            if (Date.now() < deadline && turn % 10 !== 9) {
                const after = await page.evaluate("({ schedules: __stability.schedules, permissions: __stability.permissions })");
                assert.ok(after.permissions > idleBefore.permissions && after.schedules > idleBefore.schedules, "Polling stopped");
            }
            turn++;
        }
    } catch (error) { failures.push(error.stack); throw error; }
    finally {
        fs.writeFileSync(prefix + ".json", JSON.stringify({ requestedMinutes: minutes, elapsedSeconds: started ? (Date.now() - started) / 1000 : 0,
            passed: failures.length === 0 && Date.now() >= deadline, failures, samples }, null, 2));
        await page.close();
    }
});
